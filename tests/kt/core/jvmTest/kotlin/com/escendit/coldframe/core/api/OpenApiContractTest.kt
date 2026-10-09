package com.escendit.coldframe.core.api

import kotlinx.serialization.descriptors.SerialDescriptor
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.io.File
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

/**
 * The Kotlin DTOs are hand-written (AD-10 deviation), so this test holds them to
 * `packages/openapi/coldframe.openapi.json`: every operation, path, method, header and property
 * the core uses must exist there.
 */
class OpenApiContractTest {
    private val contract: JsonObject by lazy {
        val file =
            generateSequence(File("").absoluteFile) { it.parentFile }
                .map { it.resolve("packages/openapi/coldframe.openapi.json") }
                .firstOrNull { it.isFile }
        assertNotNull(file, "coldframe.openapi.json not found above ${File("").absolutePath}")
        Json.parseToJsonElement(file.readText()).jsonObject
    }

    private fun operation(
        path: String,
        method: String,
    ): JsonObject {
        val operation =
            contract["paths"]
                ?.jsonObject
                ?.get(path)
                ?.jsonObject
                ?.get(method)
                ?.jsonObject
        assertNotNull(operation, "$method $path is not in the contract")
        return operation
    }

    private fun schema(name: String): JsonObject {
        val schema =
            contract["components"]
                ?.jsonObject
                ?.get("schemas")
                ?.jsonObject
                ?.get(name)
                ?.jsonObject
        assertNotNull(schema, "schema $name is not in the contract")
        return schema
    }

    private fun assertMirrors(
        schemaName: String,
        descriptor: SerialDescriptor,
    ) {
        val schema = schema(schemaName)
        val properties = schema["properties"]!!.jsonObject.keys
        val required =
            schema["required"]
                ?.jsonArray
                ?.map { it.jsonPrimitive.content }
                .orEmpty()
                .toSet()
        val used = (0 until descriptor.elementsCount).map { descriptor.getElementName(it) }
        assertTrue(properties.containsAll(used), "$schemaName lacks ${used - properties}")
        // The DTO may tolerate a missing property, but never insists on one the contract leaves optional.
        val insisted =
            (0 until descriptor.elementsCount)
                .filterNot { descriptor.isElementOptional(it) }
                .map { descriptor.getElementName(it) }
        assertTrue(required.containsAll(insisted), "$schemaName: the DTO requires ${insisted - required}")
    }

    @Test
    fun listSitesIsGetSites() {
        assertEquals("listSites", operation("/sites", "get")["operationId"]!!.jsonPrimitive.content)
    }

    @Test
    fun createSiteIsPostSitesWithTheIdempotencyKeyHeader() {
        val operation = operation("/sites", "post")
        assertEquals("createSite", operation["operationId"]!!.jsonPrimitive.content)
        val parameters = operation["parameters"] as JsonArray
        val reference =
            parameters
                .single()
                .jsonObject["\$ref"]!!
                .jsonPrimitive.content
        val header =
            contract["components"]!!
                .jsonObject["parameters"]!!
                .jsonObject[reference.substringAfterLast('/')]!!
                .jsonObject
        assertEquals(ColdframeApi.IDEMPOTENCY_KEY, header["name"]!!.jsonPrimitive.content)
        assertEquals("header", header["in"]!!.jsonPrimitive.content)
    }

    private fun parameterNames(operation: JsonObject): List<String> =
        (operation["parameters"] as JsonArray).map { parameter ->
            val inline = parameter.jsonObject["name"]?.jsonPrimitive?.content
            if (inline != null) return@map inline
            val reference = parameter.jsonObject["\$ref"]!!.jsonPrimitive.content
            contract["components"]!!
                .jsonObject["parameters"]!!
                .jsonObject[reference.substringAfterLast('/')]!!
                .jsonObject["name"]!!
                .jsonPrimitive.content
        }

    @Test
    fun theSiteAndLotOperationsExistWithTheirIdsAndParameters() {
        val expected =
            listOf(
                Triple("/sites/{siteId}", "patch", "renameSite") to listOf("siteId"),
                Triple("/sites/{siteId}/lots", "get", "listLots") to listOf("siteId"),
                Triple("/sites/{siteId}/lots", "post", "createLot") to listOf("siteId", ColdframeApi.IDEMPOTENCY_KEY),
                Triple("/sites/{siteId}/lots/{lotId}", "patch", "renameLot") to listOf("siteId", "lotId"),
                Triple("/sites/{siteId}/lots/{lotId}", "delete", "removeLot") to listOf("siteId", "lotId"),
            )
        for ((key, parameters) in expected) {
            val (path, method, id) = key
            val operation = operation(path, method)
            assertEquals(id, operation["operationId"]!!.jsonPrimitive.content)
            assertEquals(parameters, parameterNames(operation), "$method $path")
        }
        assertNotNull(operation("/sites/{siteId}/lots/{lotId}", "delete")["responses"]!!.jsonObject["204"])
    }

    @Test
    fun theEnrolmentOperationsExistWithTheirIdsAndParameters() {
        assertEquals("getEnrolmentKey", operation("/enrolment-key", "get")["operationId"]!!.jsonPrimitive.content)
        val enrol = operation("/sites/{siteId}/devices", "post")
        assertEquals("enrolDevice", enrol["operationId"]!!.jsonPrimitive.content)
        assertEquals(listOf("siteId", ColdframeApi.IDEMPOTENCY_KEY), parameterNames(enrol))
        val responses = enrol["responses"]!!.jsonObject
        for (status in listOf("201", "400", "401", "403", "404", "409", "422")) {
            assertNotNull(responses[status], "enrolDevice $status")
        }
        val kinds = schema("DeviceKind")["enum"]!!.jsonArray.map { it.jsonPrimitive.content }
        assertTrue(com.escendit.coldframe.core.setup.HubSetupEngine.DEVICE_KIND_HUB in kinds)
        assertTrue(com.escendit.coldframe.core.setup.NodeSetupEngine.DEVICE_KIND_NODE in kinds)
        // A Node's Lot is optional on the request and on the answer.
        for (name in listOf("EnrolDeviceRequest", "Device")) {
            assertNotNull(schema(name)["properties"]!!.jsonObject["lotId"], "$name.lotId")
        }
        assertTrue(EnrolDeviceRequestDto.serializer().descriptor.isElementOptional(4))
    }

    @Test
    fun uxDr65TheDevicesListIsAGetForMembersWithTheServersOnlineFlag() {
        val list = operation("/sites/{siteId}/devices", "get")
        assertEquals("listDevices", list["operationId"]!!.jsonPrimitive.content)
        assertEquals("Member", list["x-coldframe-minimum-role"]!!.jsonPrimitive.content)
        assertEquals(listOf("siteId"), parameterNames(list))
        val responses = list["responses"]!!.jsonObject
        for (status in listOf("200", "401", "403", "404")) {
            assertNotNull(responses[status], "listDevices $status")
        }
        val item = schema("DeviceListItem")
        assertEquals(
            setOf("id", "kind", "online"),
            item["required"]!!.jsonArray.map { it.jsonPrimitive.content }.toSet(),
        )
        assertEquals(
            setOf("id", "kind", "lotId", "lastSeenAt", "online", "lotName", "batteryPercent", "charging"),
            item["properties"]!!.jsonObject.keys,
        )
        val kinds = schema("DeviceKind")["enum"]!!.jsonArray.map { it.jsonPrimitive.content }
        assertTrue(com.escendit.coldframe.core.devices.DevicesEngine.KIND_HUB in kinds)
    }

    @Test
    fun uxDr25TheAlertsListIsAGetForMembersWithTheCursorQuery() {
        val list = operation("/sites/{siteId}/alerts", "get")
        assertEquals("listAlerts", list["operationId"]!!.jsonPrimitive.content)
        assertEquals("Member", list["x-coldframe-minimum-role"]!!.jsonPrimitive.content)
        assertEquals(listOf("siteId", "cursor", "limit"), parameterNames(list))
        val responses = list["responses"]!!.jsonObject
        for (status in listOf("200", "400", "401", "403", "404")) {
            assertNotNull(responses[status], "listAlerts $status")
        }
    }

    @Test
    fun uxDr25TheAlertDtosMirrorTheContractAndCarryEveryProperty() {
        assertMirrors("Alert", AlertDto.serializer().descriptor)
        assertMirrors("AlertList", AlertListDto.serializer().descriptor)
        for ((name, descriptor) in listOf(
            "Alert" to AlertDto.serializer().descriptor,
            "AlertList" to AlertListDto.serializer().descriptor,
        )) {
            assertEquals(
                schema(name)["properties"]!!.jsonObject.keys,
                (0 until descriptor.elementsCount).map { descriptor.getElementName(it) }.toSet(),
                name,
            )
        }
    }

    @Test
    fun uxDr26TheAlertKindsAndSidesAreTheContractValues() {
        val kinds = schema("AlertKind")["x-extensible-enum"]!!.jsonArray.map { it.jsonPrimitive.content }
        assertEquals(
            listOf(
                com.escendit.coldframe.core.alerts.AlertSummary.KIND_THRESHOLD,
                com.escendit.coldframe.core.alerts.AlertSummary.KIND_SILENT,
                com.escendit.coldframe.core.alerts.AlertSummary.KIND_BATTERY,
                com.escendit.coldframe.core.alerts.AlertSummary.KIND_UNCALIBRATED,
            ),
            kinds,
        )
        assertEquals(
            com.escendit.coldframe.core.alerts.AlertSide.entries
                .map { it.key },
            schema("AlertSide")["enum"]!!.jsonArray.map { it.jsonPrimitive.content },
        )
    }

    @Test
    fun theLotStatusesAreTheContractValuesInTheServerOrder() {
        val statuses = schema("LotStatus")["enum"]!!.jsonArray.map { it.jsonPrimitive.content }
        assertEquals(
            com.escendit.coldframe.core.lots.LotStatus.entries
                .map { it.key },
            statuses,
        )
    }

    @Test
    fun theLotDtoCarriesEveryPropertyOfTheContractAndRequiresNoMoreThanIt() {
        val lot = schema("Lot")
        val descriptor = LotDto.serializer().descriptor
        assertEquals(
            lot["properties"]!!.jsonObject.keys,
            (0 until descriptor.elementsCount).map { descriptor.getElementName(it) }.toSet(),
        )
        assertEquals(
            setOf("id", "name", "status", "statusSince"),
            lot["required"]!!.jsonArray.map { it.jsonPrimitive.content }.toSet(),
        )
        for (name in listOf("statusSince", "lastReadingAt", "pausedUntil")) {
            assertEquals(
                "date-time",
                lot["properties"]!!
                    .jsonObject[name]!!
                    .jsonObject["format"]!!
                    .jsonPrimitive.content,
                name,
            )
        }
        assertEquals(
            com.escendit.coldframe.core.lots.LotUnknownCause.entries
                .map { it.key },
            schema("LotUnknownCause")["enum"]!!.jsonArray.map { it.jsonPrimitive.content },
        )
        assertEquals(
            com.escendit.coldframe.core.lots.LotPauseSource.entries
                .map { it.key },
            schema("LotPauseSource")["enum"]!!.jsonArray.map { it.jsonPrimitive.content },
        )
    }

    @Test
    fun theDtosMirrorTheSchemas() {
        assertMirrors("RenameSiteRequest", RenameRequestDto.serializer().descriptor)
        assertMirrors("RenameLotRequest", RenameRequestDto.serializer().descriptor)
        assertMirrors("CreateLotRequest", CreateLotRequestDto.serializer().descriptor)
        assertMirrors("Lot", LotDto.serializer().descriptor)
        assertMirrors("LotList", LotListDto.serializer().descriptor)
        assertMirrors("Site", SiteDto.serializer().descriptor)
        assertMirrors("SiteList", SiteListDto.serializer().descriptor)
        assertMirrors("CreateSiteRequest", CreateSiteRequestDto.serializer().descriptor)
        assertMirrors("ProblemDetails", ProblemDto.serializer().descriptor)
        assertMirrors("EnrolmentKey", EnrolmentKeyDto.serializer().descriptor)
        assertMirrors("EnrolDeviceRequest", EnrolDeviceRequestDto.serializer().descriptor)
        assertMirrors("Device", DeviceDto.serializer().descriptor)
        assertMirrors("DeviceListItem", DeviceListItemDto.serializer().descriptor)
        assertMirrors("DeviceList", DeviceListDto.serializer().descriptor)
        assertMirrors("NodeStatus", NodeStatusDto.serializer().descriptor)
        assertMirrors("SensorReading", SensorReadingDto.serializer().descriptor)
        assertMirrors("LotHistory", LotHistoryDto.serializer().descriptor)
        assertMirrors("LotHistoryDay", LotHistoryDayDto.serializer().descriptor)
    }

    @Test
    fun uxDr63TheLotHistoryIsAGetForMembersWithTheCursorQuery() {
        val history = operation("/sites/{siteId}/lots/{lotId}/history", "get")
        assertEquals("getLotHistory", history["operationId"]!!.jsonPrimitive.content)
        assertEquals("Member", history["x-coldframe-minimum-role"]!!.jsonPrimitive.content)
        assertEquals(listOf("siteId", "lotId", "quantity", "from", "to", "cursor", "limit"), parameterNames(history))
    }

    @Test
    fun uxDr28TheSensorEnumsAreTheContractValues() {
        assertEquals(
            com.escendit.coldframe.core.lots.SensorQuantity.entries
                .map { it.key },
            schema("SensorQuantity")["enum"]!!.jsonArray.map { it.jsonPrimitive.content },
        )
        assertEquals(
            com.escendit.coldframe.core.lots.SensorUnit.entries
                .map { it.key },
            schema("SensorUnit")["enum"]!!.jsonArray.map { it.jsonPrimitive.content },
        )
        assertEquals(
            com.escendit.coldframe.core.lots.ChargeState.entries
                .map { it.key },
            schema("ChargeState")["enum"]!!.jsonArray.map { it.jsonPrimitive.content },
        )
    }

    @Test
    fun theRolesAndProblemTypesExist() {
        val roles = schema("SiteRole")["enum"]!!.jsonArray.map { it.jsonPrimitive.content }
        assertEquals(
            com.escendit.coldframe.core.sites.SiteRole.entries
                .map { it.name }
                .toSet(),
            roles.toSet(),
        )
        val types =
            schema("ProblemDetails")["properties"]!!
                .jsonObject["type"]!!
                .jsonObject["x-extensible-enum"]!!
                .jsonArray
                .map { it.jsonPrimitive.content }
        assertTrue(ColdframeApi.PROBLEM_VALIDATION in types)
        assertTrue(ColdframeApi.PROBLEM_DEVICE_ON_ANOTHER_SITE in types)
        assertTrue(ColdframeApi.PROBLEM_DEVICE_ASSIGNED in types)
        for (slug in listOf("forbidden", "site-not-found", "lot-not-found", "lot-claimed", "idempotency-key-reused")) {
            assertTrue("urn:coldframe:problem:$slug" in types, slug)
        }
    }

    @Test
    fun story52TheCalibrationOperationsExistForAdministratorsWithTheirProblems() {
        val read = operation("/sites/{siteId}/sensors/{sensorId}/calibration", "get")
        val write = operation("/sites/{siteId}/sensors/{sensorId}/calibration", "post")
        assertEquals("getSensorCalibration", read["operationId"]!!.jsonPrimitive.content)
        assertEquals("calibrateSensor", write["operationId"]!!.jsonPrimitive.content)
        for (operation in listOf(read, write)) {
            assertEquals("Administrator", operation["x-coldframe-minimum-role"]!!.jsonPrimitive.content)
            assertEquals(listOf("siteId", "sensorId"), parameterNames(operation))
        }
        assertNotNull(read["responses"]!!.jsonObject["403"])
        assertNotNull(write["responses"]!!.jsonObject["503"])
        assertTrue("urn:coldframe:problem:calibration-not-delivered" == ColdframeApi.PROBLEM_CALIBRATION_NOT_DELIVERED)
    }

    @Test
    fun story52TheCalibrationDtosMirrorTheContract() {
        assertMirrors("CalibrationState", CalibrationStateDto.serializer().descriptor)
        assertMirrors("Calibration", CalibrationDto.serializer().descriptor)
        assertMirrors("CalibrationValue", CalibrationValueDto.serializer().descriptor)
        assertMirrors("CalibrationReading", CalibrationReadingDto.serializer().descriptor)
        assertMirrors("CalibrationPoint", CalibrationPointDto.serializer().descriptor)
        assertMirrors("CalibrateSensorRequest", CalibrateSensorRequestDto.serializer().descriptor)
        assertMirrors("SensorReading", SensorReadingDto.serializer().descriptor)
    }

    @Test
    fun story54TheThresholdOperationsExistReadForMembersWriteForAdministrators() {
        val read = operation("/sites/{siteId}/sensors/{sensorId}/thresholds", "get")
        val write = operation("/sites/{siteId}/sensors/{sensorId}/thresholds", "put")
        assertEquals("getSensorThresholds", read["operationId"]!!.jsonPrimitive.content)
        assertEquals("setSensorThresholds", write["operationId"]!!.jsonPrimitive.content)
        assertEquals("Member", read["x-coldframe-minimum-role"]!!.jsonPrimitive.content)
        assertEquals("Administrator", write["x-coldframe-minimum-role"]!!.jsonPrimitive.content)
        for (operation in listOf(read, write)) {
            assertEquals(listOf("siteId", "sensorId"), parameterNames(operation))
        }
        assertNotNull(write["responses"]!!.jsonObject["403"])
    }

    @Test
    fun story54TheThresholdDtosMirrorTheContract() {
        assertMirrors("SensorThresholds", SensorThresholdsDto.serializer().descriptor)
        assertMirrors("ThresholdSide", ThresholdSideDto.serializer().descriptor)
        assertMirrors("SetSensorThresholdsRequest", SetSensorThresholdsRequestDto.serializer().descriptor)
    }

    @Test
    fun story54TheLotCarriesTheCalibratedPercentageAndLowThresholdAsOptionalNumbers() {
        val properties = schema("Lot")["properties"]!!.jsonObject
        assertNotNull(properties["moisturePercent"])
        assertNotNull(properties["lowThresholdPercent"])
    }

    @Test
    fun story63TheNotificationSettingsOperationsExistWithTheirMinimumRoles() {
        val expected =
            listOf(
                Triple("/me/notification-settings", "get", "getMyNotificationSettings") to "Authenticated",
                Triple("/me/notification-settings", "patch", "updateMyNotificationSettings") to "Authenticated",
                Triple("/sites/{siteId}/notification-settings", "get", "getSiteNotificationSettings") to "Member",
                Triple("/sites/{siteId}/notification-settings", "put", "setSiteNotificationSettings") to "Member",
                Triple("/sites/{siteId}/reminder-cadence", "get", "getSiteReminderCadence") to "Member",
                Triple("/sites/{siteId}/reminder-cadence", "put", "setSiteReminderCadence") to "Administrator",
            )
        for ((key, role) in expected) {
            val (path, method, id) = key
            val operation = operation(path, method)
            assertEquals(id, operation["operationId"]!!.jsonPrimitive.content)
            assertEquals(role, operation["x-coldframe-minimum-role"]!!.jsonPrimitive.content, id)
            val parameters = if (operation["parameters"] == null) emptyList() else parameterNames(operation)
            assertEquals(if (path.startsWith("/sites")) listOf("siteId") else emptyList(), parameters, id)
            assertNotNull(operation["responses"]!!.jsonObject["200"], id)
            assertNotNull(operation["responses"]!!.jsonObject["401"], id)
        }
        val setCadence = operation("/sites/{siteId}/reminder-cadence", "put")["responses"]!!.jsonObject
        for (status in listOf(
            "400",
            "403",
            "404",
            "503",
        )) {
            assertNotNull(setCadence[status], "setSiteReminderCadence $status")
        }
        assertNotNull(operation("/me/notification-settings", "patch")["responses"]!!.jsonObject["400"])
    }

    @Test
    fun story63TheNotificationSettingsDtosMirrorTheContractAndCarryEveryProperty() {
        val mirrors =
            listOf(
                "NotificationSettings" to NotificationSettingsDto.serializer().descriptor,
                "NotificationWindow" to NotificationWindowDto.serializer().descriptor,
                "UpdateNotificationSettingsRequest" to UpdateNotificationSettingsRequestDto.serializer().descriptor,
                "SiteNotificationSettings" to SiteNotificationSettingsDto.serializer().descriptor,
                "SetSiteNotificationSettingsRequest" to SetSiteNotificationSettingsRequestDto.serializer().descriptor,
                "SiteReminderCadence" to SiteReminderCadenceDto.serializer().descriptor,
            )
        for ((name, descriptor) in mirrors) {
            assertMirrors(name, descriptor)
            assertEquals(
                schema(name)["properties"]!!.jsonObject.keys,
                (0 until descriptor.elementsCount).map { descriptor.getElementName(it) }.toSet(),
                name,
            )
        }
    }

    @Test
    fun story63TheWindowOfAnUpdateRequiresOnlyFrom() {
        val window =
            schema("UpdateNotificationSettingsRequest")["properties"]!!
                .jsonObject["window"]!!
                .jsonObject
        val descriptor = NotificationWindowRequestDto.serializer().descriptor
        val names = (0 until descriptor.elementsCount).map { descriptor.getElementName(it) }
        assertEquals(window["properties"]!!.jsonObject.keys, names.toSet())
        assertEquals(listOf("from"), window["required"]!!.jsonArray.map { it.jsonPrimitive.content })
        assertEquals(
            listOf("from"),
            (0 until descriptor.elementsCount).filterNot { descriptor.isElementOptional(it) }.map { names[it] },
        )
    }

    @Test
    fun story63TheReminderCadencesAndTheNotDeliveredProblemAreTheContractValues() {
        assertEquals(
            com.escendit.coldframe.core.notifications.ReminderCadence.entries
                .map { it.key },
            schema("ReminderCadence")["enum"]!!.jsonArray.map { it.jsonPrimitive.content },
        )
        val types =
            schema("ProblemDetails")["properties"]!!
                .jsonObject["type"]!!
                .jsonObject["x-extensible-enum"]!!
                .jsonArray
                .map { it.jsonPrimitive.content }
        assertTrue(ColdframeApi.PROBLEM_REMINDER_CADENCE_NOT_DELIVERED in types)
        val pattern =
            schema("NotificationWindow")["properties"]!!
                .jsonObject["from"]!!
                .jsonObject["pattern"]!!
                .jsonPrimitive.content
        for (time in listOf("00:00", "07:00", "23:59")) {
            assertTrue(Regex(pattern).matches(time), time)
            assertNotNull(
                com.escendit.coldframe.core.notifications.NotificationWindow
                    .minutesOf(time),
                time,
            )
        }
        for (time in listOf("7:00", "24:00", "07:60")) {
            assertTrue(!Regex(pattern).matches(time), time)
            assertEquals(
                null,
                com.escendit.coldframe.core.notifications.NotificationWindow
                    .minutesOf(time),
                time,
            )
        }
    }
}
