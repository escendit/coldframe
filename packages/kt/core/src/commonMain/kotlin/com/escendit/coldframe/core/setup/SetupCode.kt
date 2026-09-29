package com.escendit.coldframe.core.setup

import com.escendit.coldframe.core.crypto.CryptoSpec

/**
 * The setup code as typed (AD-25). Format-agnostic entry: the app drops every whitespace
 * character and dash, uppercases it and maps the Crockford look-alikes `O → 0`, `I → 1`, `L → 1`. The code is
 * never sent over BLE or logged; it only salts the session key.
 */
public object SetupCode {
    /** The code the session key is derived with. */
    public fun normalize(input: String): String =
        buildString {
            for (char in input) {
                if (char.isWhitespace() || char in DASHES) continue
                when (val upper = char.uppercaseChar()) {
                    'O' -> append('0')
                    'I', 'L' -> append('1')
                    else -> append(upper)
                }
            }
        }

    /** Hyphen-minus, hyphen, non-breaking hyphen, en dash and em dash. */
    private val DASHES = setOf('-', '\u2010', '\u2011', '\u2013', '\u2014')

    /** Whether [normalized] can salt a session: 1 to 64 ASCII characters. */
    public fun isUsable(normalized: String): Boolean =
        normalized.isNotEmpty() &&
            normalized.length <= CryptoSpec.SETUP_MAX_CODE_LENGTH &&
            normalized.all { it.code in 0x21..0x7e }
}
