package app.com.tmlpocket.feature.releases

enum class TmlReleaseSource(val label: String, val catalog: String, private val repository: String, private val defaultId: Long) {
    GITHUB("GitHub", "https://api.github.com/repos/tModLoader/tModLoader/releases",
        "https://github.com/tModLoader/tModLoader", 432145902),
    GITEE("Gitee", "https://gitee.com/api/v5/repos/daohei/t-mod-loader-mobile/releases",
        "https://gitee.com/daohei/t-mod-loader-mobile", 1378761);

    fun key(id: Long): String = if (this == GITEE) "gitee-$id" else id.toString()

    fun defaultRelease() = TmlRelease(defaultId, DEFAULT_TAG, DEFAULT_TAG, false,
        "$repository/releases/download/$DEFAULT_TAG/tModLoader.zip", DEFAULT_SIZE, DEFAULT_DIGEST)

    companion object {
        const val DEFAULT_TAG = "v2026.09.3.1"
        const val DEFAULT_SIZE = 61159864L
        const val DEFAULT_DIGEST = "sha256:5aaed81b245044286c3ade200f8bac9162ef932ee8ff11a31e05da49a186c07b"

        fun forCountry(network: String?, region: String): TmlReleaseSource {
            val country = network?.takeIf { it.length == 2 } ?: region
            return if (country.equals("CN", ignoreCase = true)) GITEE else GITHUB
        }
    }
}
