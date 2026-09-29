package com.escendit.coldframe.core.crypto

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.io.File
import kotlin.test.assertNotNull

/** Reads `packages/crypto-spec/vectors.json`, the single source of the shared vectors. */
internal object Vectors {
    val root: JsonObject by lazy {
        val file =
            generateSequence(File("").absoluteFile) { it.parentFile }
                .map { it.resolve("packages/crypto-spec/vectors.json") }
                .firstOrNull { it.isFile }
        assertNotNull(file, "vectors.json not found above ${File("").absolutePath}")
        Json.parseToJsonElement(file.readText()).jsonObject
    }

    fun list(category: String): List<JsonObject> = root.getValue(category).jsonArray.map { it.jsonObject }

    fun anchor(name: String): JsonObject =
        root
            .getValue("anchors")
            .jsonObject
            .getValue(name)
            .jsonObject
}

internal fun JsonObject.text(field: String): String = getValue(field).jsonPrimitive.content

internal fun JsonObject.bytes(field: String): ByteArray = JdkCrypto.unhex(text(field))

/** A decimal-string unsigned 64-bit field, as the bits of a Long. */
internal fun JsonObject.number(field: String): Long = text(field).toULong().toLong()

internal fun JsonElement.int(): Int = jsonPrimitive.content.toInt()
