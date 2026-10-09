package com.escendit.coldframe.core.push

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** Story 6.5: the push payload of `packages/asyncapi` as the core reads it, on both platforms. */
class PushPayloadTest {
    private val alert =
        mapOf(
            "kind" to "alert",
            "siteId" to "site-a",
            "siteName" to "Home garden",
            "lotId" to "lot-1",
            "alertId" to "alert-1",
            "collapseId" to "c1",
            "title" to "Tomatoes needs water",
            "body" to "~20 % in the soil, your low is 30 %.",
        )

    @Test
    fun uxDr115AnFcmDataMessageIsReadAsItIsSent() {
        val payload = PushPayload.parse(alert)

        assertEquals(
            PushPayload(
                kind = "alert",
                siteId = "site-a",
                lotId = "lot-1",
                alertId = "alert-1",
                collapseId = "c1",
                title = "Tomatoes needs water",
                body = "~20 % in the soil, your low is 30 %.",
                siteName = "Home garden",
            ),
            payload,
        )
    }

    @Test
    fun uxDr120AnAlertAndAReminderLeadToTheLotAndASummaryToTheOverview() {
        assertEquals(PushTap("alert", "site-a", "lot-1"), PushPayload.parse(alert)?.tap)
        assertTrue(PushTap("alert", "site-a", "lot-1").opensLot)
        assertTrue(PushTap("reminder", "site-a", "lot-1").opensLot)
        assertFalse(PushTap("summary", "site-a", null).opensLot)
        // An Alert that names no Lot has only the overview to open.
        assertFalse(PushTap("alert", "site-a", null).opensLot)
    }

    @Test
    fun uxDr120AKindThisAppDoesNotKnowLeadsToTheOverviewOfItsSite() {
        val tap = PushPayload.parse(alert + ("kind" to "health"))?.tap

        assertEquals("health", tap?.kind)
        assertEquals("site-a", tap?.siteId)
        assertFalse(tap!!.opensLot)
    }

    @Test
    fun theApnsColdframeObjectCarriesNoTextAndIsReadTheSameWay() {
        val coldframe = alert - "title" - "body" - "siteName"

        val payload = PushPayload.parse(coldframe)

        assertEquals(PushTap("alert", "site-a", "lot-1"), payload?.tap)
        assertNull(payload?.title)
        assertNull(payload?.body)
        assertNull(payload?.siteName)
    }

    @Test
    fun aMessageWithoutAKindOrASiteIsNotOurs() {
        assertNull(PushPayload.parse(alert - "kind"))
        assertNull(PushPayload.parse(alert - "siteId"))
        assertNull(PushPayload.parse(alert + ("siteId" to " ")))
        assertNull(PushPayload.parse(emptyMap()))
    }

    @Test
    fun emptyOptionalKeysCountAsAbsent() {
        val payload = PushPayload.parse(alert + ("lotId" to "") + ("collapseId" to ""))

        assertNull(payload?.lotId)
        assertNull(payload?.collapseId)
    }
}
