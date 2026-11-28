package app.com.tmlpocket.feature.releases

import app.com.tmlpocket.feature.releases.TmlReleaseClient.DownloadError
import app.com.tmlpocket.feature.releases.TmlReleaseClient.DownloadException
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.io.InterruptedIOException
import java.net.HttpURLConnection
import java.nio.file.Files
import java.nio.file.StandardCopyOption.ATOMIC_MOVE
import java.nio.file.StandardCopyOption.REPLACE_EXISTING
import java.security.MessageDigest
import java.time.ZonedDateTime
import java.time.format.DateTimeFormatter
import java.time.format.DateTimeParseException
import java.util.UUID
import java.util.concurrent.TimeUnit
import java.util.concurrent.locks.ReentrantLock
import org.json.JSONException
import org.json.JSONObject

/** The .part length is the checkpoint; metadata describes its representation, never a byte count. */
internal class ResumableReleaseDownload(
    private val release: TmlRelease,
    cacheKey: String,
    private val cacheDir: File,
    private val connect: (String) -> HttpURLConnection,
    private val cancelled: () -> Boolean,
    private val progress: (Long, Long) -> Unit
) {
    private val partial = File(cacheDir, "tml-$cacheKey.part")
    private val metadata = File(cacheDir, "tml-$cacheKey.json")
    private val pendingMetadata = File(cacheDir, "tml-$cacheKey.json.tmp")

    fun download(): File {
        // Shared across clients, with cancellation while waiting. Import owns only the returned ZIP.
        try {
            while (true) {
                checkCancelled()
                if (downloadLock.tryLock(100, TimeUnit.MILLISECONDS)) break
            }
        } catch (error: InterruptedException) {
            Thread.currentThread().interrupt()
            throw Cancelled()
        }
        try {
            checkCancelled()
            storage {
                if (!cacheDir.isDirectory && !cacheDir.mkdirs()) throw IOException("无法创建下载缓存")
            }
            return transfer()
        } catch (error: DownloadException) {
            if (error.reason == DownloadError.VALIDATION || error.reason == DownloadError.DIGEST) {
                try {
                    discardPartial()
                } catch (cleanup: IOException) {
                    error.addSuppressed(cleanup)
                }
            }
            throw error
        } catch (error: Cancelled) {
            throw error
        } catch (error: IOException) {
            throw DownloadException(DownloadError.NETWORK, "下载中断，可重试继续下载", cause = error)
        } finally {
            downloadLock.unlock()
        }
    }

    private fun transfer(): File {
        val expectedDigest = release.digest?.let {
            validate(it.matches(Regex("sha256:[0-9a-fA-F]{64}")), "不支持的发行资产摘要")
            it.substringAfter(':').lowercase()
        }
        validate(release.size >= -1, "发行资产长度无效")
        var state = readMetadata(expectedDigest)
        var received = if (state != null) partial.length() else 0L
        if (state != null && received > state.total) {
            invalid("缓存长度超过发行资产长度")
        }
        // Without an If-Range validator, only a complete file authenticated by SHA-256 is reusable.
        if (state != null && state.validator == null && !(received == state.total && expectedDigest != null)) {
            discardPartial()
            state = null
            received = 0
        }
        val hash = MessageDigest.getInstance("SHA-256")
        if (state != null) {
            rehash(hash)
            progress(received, state.total)
            if (received == state.total && expectedDigest != null) {
                return finish(hash, expectedDigest, state.total)
            }
        }

        var restartedAfter416 = false
        while (true) {
            checkCancelled()
            val offset = received
            val previous = state
            val resume = offset > 0 && previous?.validator != null
            val connection = connect(release.downloadUrl)
            try {
                connection.setRequestProperty("Accept", "application/octet-stream")
                connection.setRequestProperty("Accept-Encoding", "identity")
                if (resume) {
                    connection.setRequestProperty("Range", "bytes=$offset-")
                    connection.setRequestProperty("If-Range", previous.validator.value)
                }
                val code = connection.responseCode
                checkCancelled()
                validate(connection.url.protocol == "https", "拒绝非 HTTPS 重定向")
                if (code == 416) {
                    validate(resume && !restartedAfter416, "服务器返回了无效的范围响应")
                    val total = Regex("bytes \\*/([0-9]+)").matchEntire(header(connection, "Content-Range") ?: "")
                        ?.groupValues?.get(1)?.toLongOrNull()
                    validate(total != null, "范围响应缺少有效的总长度")
                    if (total == offset && total == previous!!.total && (release.size < 0 || total == release.size) &&
                        (expectedDigest != null || validatorMatches(connection, previous.validator!!))) {
                        return finish(hash, expectedDigest, total)
                    }
                    // A stale range is never proof of completion. Retry once as a fresh GET.
                    discardPartial()
                    state = null
                    received = 0
                    hash.reset()
                    restartedAfter416 = true
                    continue
                }
                if (code != 200 && code != 206) {
                    if (code in 200..299) invalid("下载响应状态无效（HTTP $code）")
                    throw DownloadException(
                        DownloadError.HTTP,
                        if (code == 403 || code == 429) "下载源请求受限，请稍后重试；仍可导入本地 ZIP"
                        else "下载源请求失败（HTTP $code），请重试",
                        httpStatus = code
                    )
                }
                val encoding = header(connection, "Content-Encoding")
                validate(encoding == null || encoding.equals("identity", true), "下载响应使用了不支持的内容编码")
                val contentLength = contentLength(connection)
                val responseBytes: Long
                if (code == 206) {
                    validate(resume, "服务器返回了未请求的部分内容")
                    val range = parseRange(header(connection, "Content-Range"))
                    validate(range.start == offset && range.total == previous!!.total &&
                        (release.size < 0 || range.total == release.size), "下载范围与缓存或发行资产不符")
                    validate(validatorMatches(connection, previous!!.validator!!), "下载验证标识已改变或缺失")
                    responseBytes = range.end - range.start + 1
                    validate(contentLength < 0 || contentLength == responseBytes, "下载范围与响应长度不符")
                    state = previous
                } else {
                    validate(header(connection, "Content-Range") == null, "完整响应含有部分内容范围")
                    validate(contentLength < 0 || release.size < 0 || contentLength == release.size,
                        "下载长度与发行资产不符")
                    val total = if (release.size >= 0) release.size else contentLength
                    validate(total >= 0, "下载响应缺少可验证的总长度")
                    // Remove old metadata BEFORE replacing bytes, so a crash cannot mix representations.
                    discardPartial()
                    received = 0
                    hash.reset()
                    state = Metadata(total, responseValidator(connection))
                    writeMetadata(state, expectedDigest)
                    responseBytes = total
                    progress(0, total)
                }
                val total = state.total
                var bodyBytes = 0L
                connection.inputStream.use { input ->
                    storage { FileOutputStream(partial, code == 206) }.use { output ->
                        try {
                            val buffer = ByteArray(64 * 1024)
                            while (true) {
                                checkCancelled()
                                val count = input.read(buffer)
                                checkCancelled()
                                if (count < 0) break
                                validate(count.toLong() <= responseBytes - bodyBytes && count.toLong() <= total - received,
                                    "下载数据超过发行资产或响应范围长度")
                                storage { output.write(buffer, 0, count) }
                                hash.update(buffer, 0, count)
                                bodyBytes += count
                                received += count
                                progress(received, total)
                            }
                        } finally {
                            // Persist bytes even when paused or disconnected. Metadata was committed first.
                            storage { output.fd.sync() }
                        }
                    }
                }
                checkCancelled()
                if (bodyBytes != responseBytes) {
                    throw DownloadException(DownloadError.NETWORK, "下载不完整，可重试继续下载")
                }
                if (received == total) return finish(hash, expectedDigest, total)
                // Some servers cap a valid 206 response. Continue from its exact end.
                if (state.validator == null) invalid("部分响应缺少可用于继续下载的验证标识")
            } finally {
                connection.disconnect()
            }
        }
    }

    private fun rehash(hash: MessageDigest) = storage {
        partial.inputStream().use { input ->
            val buffer = ByteArray(64 * 1024)
            while (true) {
                checkCancelled()
                val count = input.read(buffer)
                if (count < 0) break
                hash.update(buffer, 0, count)
            }
        }
    }

    private fun finish(hash: MessageDigest, expectedDigest: String?, total: Long): File {
        checkCancelled()
        validate(partial.length() == total, "下载文件长度校验失败")
        val actual = hash.digest().joinToString("") { "%02x".format(it.toInt() and 255) }
        if (expectedDigest != null && actual != expectedDigest) {
            throw DownloadException(DownloadError.DIGEST, "SHA-256 校验失败，请重试")
        }
        checkCancelled()
        return storage {
            // Unique handoff prevents a later download from changing a file still being imported.
            val result = File(cacheDir, "tml-${release.id}-${UUID.randomUUID()}.zip")
            Files.move(partial.toPath(), result.toPath(), ATOMIC_MOVE)
            // Stale metadata without a .part is ignored on the next attempt.
            metadata.delete()
            pendingMetadata.delete()
            result
        }
    }

    private data class Validator(val header: String, val value: String)
    private data class Metadata(val total: Long, val validator: Validator?)
    private data class ContentRange(val start: Long, val end: Long, val total: Long)

    private fun readMetadata(expectedDigest: String?): Metadata? {
        val saved = try {
            if (!partial.isFile || !metadata.isFile) null else storage {
                val bytes = metadata.inputStream().use { input ->
                    val output = ByteArrayOutputStream()
                    val buffer = ByteArray(4096)
                    while (output.size() <= MAX_METADATA_BYTES) {
                        val count = input.read(buffer, 0, minOf(buffer.size, MAX_METADATA_BYTES + 1 - output.size()))
                        if (count < 0) break
                        output.write(buffer, 0, count)
                    }
                    output.toByteArray()
                }
                if (bytes.size > MAX_METADATA_BYTES) return@storage null
                val json = JSONObject(String(bytes, Charsets.UTF_8))
                if (json.getInt("version") != 1 || json.getString("releaseId") != release.id.toString() ||
                    json.getString("url") != release.downloadUrl || json.getString("size") != release.size.toString() ||
                    json.getString("digest") != (expectedDigest ?: "")) return@storage null
                val total = json.getString("total").toLongOrNull() ?: return@storage null
                if (total < 0 || (release.size >= 0 && total != release.size)) return@storage null
                val kind = json.getString("validatorHeader")
                val value = json.getString("validatorValue")
                val validator = when {
                    kind == "ETag" && isStrongEtag(value) -> Validator(kind, value)
                    kind == "Last-Modified" && httpDate(value) != null -> Validator(kind, value)
                    kind.isEmpty() && value.isEmpty() -> null
                    else -> return@storage null
                }
                Metadata(total, validator)
            }
        } catch (_: JSONException) {
            null
        }
        if (saved == null) discardPartial()
        return saved
    }

    private fun writeMetadata(state: Metadata, expectedDigest: String?) = storage {
        val json = JSONObject()
            .put("version", 1).put("releaseId", release.id.toString()).put("url", release.downloadUrl)
            .put("size", release.size.toString()).put("digest", expectedDigest ?: "")
            .put("total", state.total.toString()).put("validatorHeader", state.validator?.header ?: "")
            .put("validatorValue", state.validator?.value ?: "")
        val bytes = json.toString().toByteArray(Charsets.UTF_8)
        validate(bytes.size <= MAX_METADATA_BYTES, "下载元数据过大")
        FileOutputStream(pendingMetadata).use { output ->
            output.write(bytes)
            output.fd.sync()
        }
        Files.move(pendingMetadata.toPath(), metadata.toPath(), ATOMIC_MOVE, REPLACE_EXISTING)
    }

    private fun discardPartial() = storage {
        for (file in listOf(metadata, partial, pendingMetadata)) {
            if (file.exists() && !file.delete()) throw IOException("无法清理下载缓存：${file.name}")
        }
    }

    private fun responseValidator(connection: HttpURLConnection): Validator? {
        val etag = header(connection, "ETag")
        if (etag != null) return if (isStrongEtag(etag)) Validator("ETag", etag) else null
        val modified = header(connection, "Last-Modified") ?: return null
        val modifiedAt = httpDate(modified) ?: return null
        val servedAt = header(connection, "Date")?.let(::httpDate) ?: return null
        // RFC 9110: a client may use the date for If-Range only when it is a strong validator.
        return if (modifiedAt <= servedAt - 60) Validator("Last-Modified", modified) else null
    }

    private fun validatorMatches(connection: HttpURLConnection, validator: Validator): Boolean =
        header(connection, validator.header) == validator.value

    private fun isStrongEtag(value: String): Boolean = value.length in 2..MAX_HEADER_LENGTH &&
        value.first() == '"' && value.last() == '"' &&
        value.substring(1, value.lastIndex).all { it == '\u0021' || it in '\u0023'..'\u007e' || it in '\u0080'..'\u00ff' }

    private fun httpDate(value: String): Long? = try {
        if (value.length > MAX_HEADER_LENGTH) null
        else ZonedDateTime.parse(value, DateTimeFormatter.RFC_1123_DATE_TIME).toEpochSecond()
    } catch (_: DateTimeParseException) {
        null
    }

    private fun contentLength(connection: HttpURLConnection): Long {
        val value = header(connection, "Content-Length") ?: return -1
        validate(value.matches(Regex("[0-9]+")), "下载响应长度无效")
        return value.toLongOrNull() ?: invalid("下载响应长度溢出")
    }

    private fun parseRange(value: String?): ContentRange {
        val match = Regex("bytes ([0-9]+)-([0-9]+)/([0-9]+)").matchEntire(value ?: "")
            ?: invalid("下载响应范围无效")
        val start = match.groupValues[1].toLongOrNull() ?: invalid("下载范围溢出")
        val end = match.groupValues[2].toLongOrNull() ?: invalid("下载范围溢出")
        val total = match.groupValues[3].toLongOrNull() ?: invalid("下载范围溢出")
        validate(start <= end && end < total, "下载响应范围越界")
        return ContentRange(start, end, total)
    }

    private fun header(connection: HttpURLConnection, name: String): String? {
        val value = connection.getHeaderField(name)?.trim() ?: return null
        validate(value.length <= MAX_HEADER_LENGTH && '\r' !in value && '\n' !in value, "下载响应头无效")
        return value
    }

    private fun checkCancelled() {
        if (cancelled() || Thread.currentThread().isInterrupted) throw Cancelled()
    }

    private class Cancelled : InterruptedIOException("已取消")

    private inline fun <T> storage(action: () -> T): T = try {
        action()
    } catch (error: DownloadException) {
        throw error
    } catch (error: Cancelled) {
        throw error
    } catch (error: IOException) {
        throw DownloadException(DownloadError.STORAGE, "无法读写下载缓存", cause = error)
    }

    private fun validate(valid: Boolean, message: String) {
        if (!valid) invalid(message)
    }

    private fun invalid(message: String): Nothing = throw DownloadException(DownloadError.VALIDATION, message)

    companion object {
        private val downloadLock = ReentrantLock()
        private const val MAX_METADATA_BYTES = 16 * 1024
        private const val MAX_HEADER_LENGTH = 2048
    }
}
