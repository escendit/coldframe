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
        for (slug in listOf("forbidden", "site-not-found", "lot-not-found", "lot-claimed", "idempotency-key-reused")) {
            assertTrue("urn:coldframe:problem:$slug" in types, slug)
        }
    }
}
