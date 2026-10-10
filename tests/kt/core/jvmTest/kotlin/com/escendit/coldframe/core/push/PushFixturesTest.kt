package com.escendit.coldframe.core.push

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.io.File
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

/**
 * The push payload is authored in `packages/asyncapi`; the Server's channel tests send exactly these fixtures. This
 * test holds the core to them: parsing what FCM delivers (`fcm.message.data`) and what APNs delivers
 * (`apns.payload.coldframe`) gives the fixture's `route`, where a tap must lead.
 */
class PushFixturesTest {
    private val folder: File by lazy {
        val found =
            generateSequence(File("").absoluteFile) { it.parentFile }
                .map { it.resolve("packages/asyncapi/fixtures") }
                .firstOrNull { it.isDirectory }
        assertNotNull(found, "packages/asyncapi/fixtures not found above ${File("").absolutePath}")
        found
    }

    private fun fixture(kind: String): JsonObject =
        Json.parseToJsonElement(folder.resolve("push.$kind.json").readText()).jsonObject

    private fun JsonObject.strings(): Map<String, String> = mapValues { it.value.jsonPrimitive.content }

    private fun JsonObject.at(vararg path: String): JsonObject = path.fold(this) { node, key -> node[key]!!.jsonObject }

    private fun route(fixture: JsonObject): PushTap {
        val route = fixture.at("route").strings()
        return PushTap(route.getValue("kind"), route.getValue("siteId"), route["lotId"])
    }

    private val kinds = listOf("alert", "reminder", "summary")

    @Test
    fun thereIsOneFixturePerKindAndNoOther() {
        assertEquals(kinds.map { "push.$it.json" }.sorted(), folder.list()!!.sorted())
    }

    @Test
    fun uxDr120TheFcmDataOfEveryFixtureLeadsWhereItsRouteSays() {
        for (kind in kinds) {
            val fixture = fixture(kind)
            val payload = PushPayload.parse(fixture.at("fcm", "message", "data").strings())
            assertEquals(route(fixture), payload?.tap, kind)
        }
    }

    @Test
    fun uxDr120TheApnsColdframeObjectOfEveryFixtureLeadsWhereItsRouteSays() {
        for (kind in kinds) {
            val fixture = fixture(kind)
            val payload = PushPayload.parse(fixture.at("apns", "payload", "coldframe").strings())
            assertEquals(route(fixture), payload?.tap, kind)
        }
    }

    @Test
    fun uxDr120AnAlertAndAReminderOpenTheLotAndTheSummaryTheOverview() {
        assertTrue(route(fixture("alert")).opensLot)
        assertTrue(route(fixture("reminder")).opensLot)
        assertEquals(false, route(fixture("summary")).opensLot)
    }

    @Test
    fun uxDr115TheFcmDataCarriesTheTextTheGroupNameAndTheCollapseIdentity() {
        for (kind in kinds) {
            val data = fixture(kind).at("fcm", "message", "data").strings()
            val payload = assertNotNull(PushPayload.parse(data), kind)
            assertEquals(data.getValue("title"), payload.title, kind)
            assertEquals(data.getValue("body"), payload.body, kind)
            assertEquals(data.getValue("siteName"), payload.siteName, kind)
            assertEquals(data.getValue("collapseId"), payload.collapseId, kind)
            assertEquals(data["alertId"], payload.alertId, kind)
            // Both platforms carry the same routing keys with the same values.
            val coldframe = fixture(kind).at("apns", "payload", "coldframe").strings()
            assertEquals(coldframe, data.filterKeys { it in PushPayload.ROUTING_KEYS }, kind)
        }
    }
}
