package com.escendit.coldframe.core

/**
 * The shared core owns BLE setup, OIDC, the API client and push-token registration (AD-14).
 * They arrive with their stories. Until then the module holds only what proves that the
 * workspace builds, tests and lints.
 */
public object Module {
    /** Name of this module. */
    public const val NAME: String = "coldframe-core"
}
