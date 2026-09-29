package com.escendit.coldframe.core.crypto

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

/** `packages/crypto-spec/vectors.json`, compiled into the test as [VECTORS_JSON] by Gradle. */
internal object SharedVectors {
    private val root: JsonObject by lazy { Json.parseToJsonElement(VECTORS_JSON).jsonObject }

    fun list(category: String): List<JsonObject> = root.getValue(category).jsonArray.map { it.jsonObject }

    fun anchor(name: String): JsonObject =
        root
            .getValue("anchors")
            .jsonObject
            .getValue(name)
            .jsonObject
}

internal fun JsonObject.string(field: String): String = getValue(field).jsonPrimitive.content

internal fun JsonObject.hex(field: String): ByteArray = string(field).hexToByteArray()

/** A decimal-string unsigned 64-bit field, as the bits of a Long. */
internal fun JsonObject.counter(field: String): Long = string(field).toULong().toLong()
