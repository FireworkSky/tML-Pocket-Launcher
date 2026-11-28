package app.com.tmlpocket.feature.mods

import java.io.File
import java.io.IOException
import java.io.InterruptedIOException
import java.net.HttpURLConnection
import java.net.URL
import org.json.JSONObject

/** Public Steam details only. Steam-only UGC transfers are reported explicitly. */
class WorkshopClient {
    fun details(id: String): JSONObject {
        require(id.matches(Regex("[0-9]{1,20}"))) { "请输入 Steam 创意工坊项目编号" }
        val connection =
            URL("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/")
                .openConnection() as HttpURLConnection
        connection.connectTimeout = 15_000
        connection.readTimeout = 20_000
        connection.requestMethod = "POST"
        connection.doOutput = true
        try {
            connection.outputStream.use {
                it.write("itemcount=1&publishedfileids%5B0%5D=$id".toByteArray())
            }
            require(connection.responseCode == 200) { "Steam 查询失败：HTTP ${connection.responseCode}" }
            val text = connection.inputStream.use { boundedText(it, 2 * 1024 * 1024) }
            val detail =
                JSONObject(text)
                    .getJSONObject("response")
                    .getJSONArray("publishedfiledetails")
                    .getJSONObject(0)
            require(detail.optInt("result") == 1 && detail.optInt("consumer_app_id") == 1281930) {
                "该项目不是公开可用的 tModLoader 模组"
            }
            return detail
        } finally {
            connection.disconnect()
        }
    }

    fun download(id: String, cache: File, cancelled: () -> Boolean): File {
        val detail = details(id)
        val address = detail.optString("file_url")
        require(address.isNotBlank()) {
            "此模组需要 Steam UGC 下载服务，暂不提供公开直链。请从已购游戏的 Steam 客户端下载游戏模组后导入。"
        }
        var url = URL(address)
        var connection: HttpURLConnection? = null
        repeat(6) {
            require(
                url.protocol == "https" &&
                    listOf("steamusercontent.com", "steamcontent.com", "steamcdn-a.akamaihd.net")
                        .any { url.host == it || url.host.endsWith(".$it") }
            ) {
                "Steam 返回了不受支持的文件来源"
            }
            connection =
                (url.openConnection() as HttpURLConnection).apply {
                    connectTimeout = 15_000
                    readTimeout = 20_000
                    instanceFollowRedirects = false
                }
            val active = requireNotNull(connection)
            if (active.responseCode in 300..399) {
                url = URL(url, active.getHeaderField("Location"))
                active.disconnect()
            } else return downloadBody(active, cache, cancelled)
        }
        connection?.disconnect()
        throw IOException("Steam 文件重定向过多")
    }

    private fun downloadBody(
        connection: HttpURLConnection,
        cache: File,
        cancelled: () -> Boolean,
    ): File {
        val file = File.createTempFile("workshop-", ".tmod", cache)
        var complete = false
        try {
            require(connection.responseCode == 200) { "模组下载失败：HTTP ${connection.responseCode}" }
            connection.inputStream.use { input ->
                file.outputStream().use { output ->
                    val buffer = ByteArray(64 * 1024)
                    var size = 0L
                    while (true) {
                        if (cancelled()) throw InterruptedIOException("已取消")
                        val count = input.read(buffer)
                        if (count < 0) break
                        size += count
                        require(size <= 1024L * 1024 * 1024) { "模组超过 1 GB 上限" }
                        output.write(buffer, 0, count)
                    }
                }
            }
            TmodReader.read(file, verifyHash = true)
            complete = true
            return file
        } finally {
            connection.disconnect()
            if (!complete) file.delete()
        }
    }

    private fun boundedText(input: java.io.InputStream, limit: Int): String {
        val output = java.io.ByteArrayOutputStream()
        val buffer = ByteArray(8192)
        while (true) {
            val read = input.read(buffer)
            if (read < 0) break
            require(output.size() + read <= limit) { "Steam 响应过大" }
            output.write(buffer, 0, read)
        }
        return output.toString("UTF-8")
    }
}
