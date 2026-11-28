package app.com.tmlpocket.feature.installation

import android.content.pm.PackageManager

internal enum class MobileTerrariaChannel(
    val id: String,
    val packageName: String,
    certificateSha256: String,
) {
    GOOGLE_PLAY(
        "google-play",
        "com.and.games505.TerrariaPaid",
        "df4faf826627a08e6b24489f4ef64b7330debe6c1d5dc5834bf422dfc19a8542",
    );

    private val certificate = certificateSha256.chunked(2).map { it.toInt(16).toByte() }.toByteArray()

    fun hasTrustedSignature(manager: PackageManager): Boolean =
        manager.hasSigningCertificate(packageName, certificate, PackageManager.CERT_INPUT_SHA256)
}
