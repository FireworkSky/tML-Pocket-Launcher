package app.com.tmlpocket.feature.releases

import java.io.File
import java.io.IOException
import java.io.InterruptedIOException
import java.net.HttpURLConnection
import java.net.URL
import org.json.JSONArray
import org.json.JSONObject

/** Regional release metadata and downloads. No Android dependencies; connections are injectable for tests. */
class TmlReleaseClient(
    val source: TmlReleaseSource = TmlReleaseSource.GITHUB,
    private val connect: (URL) -> HttpURLConnection = { it.openConnection() as HttpURLConnection }
) {
    fun releases(page: Int, cancelled: () -> Boolean = { false }): TmlReleasePage {
        require(page > 0)
        return parsePage(readCatalog("${source.catalog}?per_page=$PAGE_SIZE&page=$page", cancelled))
    }

    fun release(id: Long, cancelled: () -> Boolean = { false }): TmlRelease {
        require(id > 0)
        val body = readCatalog("${source.catalog}/$id", cancelled)
        return parsePage(JSONArray().put(JSONObject(body)).toString()).releases
            .firstOrNull { it.id == id } ?: throw IOException("此发行版未提供 tModLoader ZIP")
    }

    private fun readCatalog(address: String, cancelled: () -> Boolean): String {
        checkCancelled(cancelled)
        val connection = connection(address)
        try {
            checkResponse(connection)
            val body = connection.inputStream.use { input ->
                val output = java.io.ByteArrayOutputStream()
                val buffer = ByteArray(16 * 1024)
                while (true) {
                    checkCancelled(cancelled)
                    val count = input.read(buffer)
                    if (count < 0) break
                    if (output.size() + count > 4 * 1024 * 1024) throw IOException("版本列表过大")
                    output.write(buffer, 0, count)
                }
                output.toString("UTF-8")
            }
            checkCancelled(cancelled)
            return body
        } finally {
            connection.disconnect()
        }
    }

    /**
     * Pass a persistent directory to keep resumable bytes across process death.
     * The returned verified ZIP belongs to the caller, which deletes it after import.
     * Cancellation throws InterruptedIOException and retains the cached partial.
     */
    fun download(
        release: TmlRelease,
        cacheDir: File,
        cancelled: () -> Boolean = { false },
        progress: (Long, Long) -> Unit = { _, _ -> }
    ): File = ResumableReleaseDownload(release, source.key(release.id), cacheDir, ::connection, cancelled, progress).download()

    enum class DownloadError { HTTP, NETWORK, STORAGE, VALIDATION, DIGEST }

    class DownloadException(
        val reason: DownloadError,
        message: String,
        val httpStatus: Int? = null,
        cause: Throwable? = null
    ) : IOException(message, cause)

    private fun connection(address: String): HttpURLConnection {
        val url = URL(address)
        if (url.protocol != "https") throw IOException("发行下载必须使用 HTTPS")
        return connect(url).apply {
            connectTimeout = 15_000
            readTimeout = 15_000
            instanceFollowRedirects = true
            setRequestProperty("Accept", "application/json")
            setRequestProperty("User-Agent", "tModLoader-Mobile-Launcher")
            setRequestProperty("Accept-Encoding", "identity")
        }
    }

    private fun checkResponse(connection: HttpURLConnection) {
        val code = connection.responseCode
        if (connection.url.protocol != "https") throw IOException("拒绝非 HTTPS 重定向")
        if (code == 403 || code == 429) throw IOException("${source.label} 请求受限，请稍后重试；仍可导入本地 ZIP")
        if (code !in 200..299) throw IOException("${source.label} 请求失败（HTTP $code），请重试")
    }

    companion object {
        const val PAGE_SIZE = 30

        fun parsePage(body: String): TmlReleasePage {
            val array = JSONArray(body)
            val releases = mutableListOf<TmlRelease>()
            for (i in 0 until array.length()) {
                val item = array.optJSONObject(i) ?: continue
                if (item.optBoolean("draft", false)) continue
                val assets = item.optJSONArray("assets") ?: continue
                for (a in 0 until assets.length()) {
                    val asset = assets.optJSONObject(a) ?: continue
                    if (asset.optString("name") != "tModLoader.zip") continue
                    val url = asset.optString("browser_download_url")
                    val tag = item.optString("tag_name")
                    if (tag.isBlank() || !url.startsWith("https://")) continue
                    releases += TmlRelease(
                        item.optLong("id"), tag, item.optString("name").ifBlank { tag },
                        item.optBoolean("prerelease", false), url,
                        if (tag == TmlReleaseSource.DEFAULT_TAG) TmlReleaseSource.DEFAULT_SIZE else asset.optLong("size", -1),
                        if (tag == TmlReleaseSource.DEFAULT_TAG) TmlReleaseSource.DEFAULT_DIGEST
                        else if (asset.isNull("digest")) null else asset.optString("digest").takeIf { it.isNotBlank() }
                    )
                    break
                }
            }
            return TmlReleasePage(releases, array.length() == PAGE_SIZE)
        }

        private fun checkCancelled(cancelled: () -> Boolean) {
            if (cancelled() || Thread.currentThread().isInterrupted) throw InterruptedIOException("已取消")
        }
    }
}
