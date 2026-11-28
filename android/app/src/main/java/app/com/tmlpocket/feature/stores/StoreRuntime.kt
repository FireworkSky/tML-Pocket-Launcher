package app.com.tmlpocket.feature.stores

import android.content.Context
import android.os.SystemClock
import app.com.tmlpocket.core.platform.NativeCoreClr
import app.com.tmlpocket.core.runtime.AssetBootstrap
import app.com.tmlpocket.core.runtime.GameRuntime
import java.io.File
import java.io.IOException
import java.net.InetAddress
import java.net.Socket
import java.net.SocketTimeoutException
import java.util.UUID
import org.json.JSONObject

/** One managed host in the launcher process; SDL/CoreCLR game uses the :game process. */
internal object StoreRuntime {
    private val secret = UUID.randomUUID().toString() + UUID.randomUUID().toString()
    private var port = 0
    private var started = false
    @Volatile private var failure: String? = null

    @Synchronized
    private fun ensureStarted(context: Context) {
        if (port > 0) return
        check(!started) { failure ?: "商店运行时尚未就绪，请重新打开启动器" }
        started = true
        val endpoint = File(context.cacheDir, "stores-${android.os.Process.myPid()}.port")
        endpoint.delete()
        Thread(
                {
                    try {
                        val (runtime, launcher) = AssetBootstrap.prepareLauncher(context)
                        System.loadLibrary("main")
                        GameRuntime.loadAndroidCryptoProvider(runtime.absolutePath)
                        for ((name, value) in
                            mapOf(
                                "HOME" to context.filesDir.absolutePath,
                                "TMPDIR" to context.cacheDir.absolutePath,
                                "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT" to "1",
                            )) {
                            check(NativeCoreClr.nativeSetEnv(name, value) == 0)
                        }
                        val root = File(context.cacheDir, "store-downloads").apply { mkdirs() }
                        val result =
                            NativeCoreClr.nativeLaunch(
                                File(launcher, "Stores/MobileLauncher.Stores.dll").absolutePath,
                                arrayOf(root.absolutePath, secret, endpoint.absolutePath),
                                runtime.absolutePath,
                            )
                        failure = "商店运行时已退出（$result）：${NativeCoreClr.nativeLastError()}"
                    } catch (error: Throwable) {
                        failure = "商店运行时启动失败：${error.message}"
                    }
                },
                "managed-stores",
            )
            .apply {
                isDaemon = true
                start()
            }
        val deadline = SystemClock.elapsedRealtime() + 90_000
        while (SystemClock.elapsedRealtime() < deadline) {
            failure?.let { throw IOException(it) }
            val candidate = runCatching { endpoint.readText().trim().toInt() }.getOrNull()
            if (candidate != null && candidate in 1..65535) {
                port = candidate
                endpoint.delete()
                return
            }
            Thread.sleep(80)
        }
        throw IOException("商店运行时准备超时，请重新打开启动器")
    }

    fun request(
        context: Context,
        method: String,
        params: JSONObject = JSONObject(),
        cancelled: () -> Boolean = { false },
        progress: (String, Int) -> Unit = { _, _ -> },
    ): Any {
        ensureStarted(context.applicationContext)
        val socket = Socket()
        socket.use {
            it.connect(java.net.InetSocketAddress(InetAddress.getByName("127.0.0.1"), port), 5_000)
            it.soTimeout = 1000
            val output = it.getOutputStream().bufferedWriter(Charsets.UTF_8)
            output.write(
                JSONObject()
                    .put("secret", secret)
                    .put("method", method)
                    .put("params", params)
                    .toString()
            )
            output.newLine()
            output.flush()
            val input = it.getInputStream().buffered()
            val partial = java.io.ByteArrayOutputStream()
            val responseLimit = if (method == "tml.resources") 2_000_000 else 100_000
            val deadline =
                SystemClock.elapsedRealtime() +
                    if (method.endsWith("download") || method == "gog.import") 7_500_000
                    else 120_000
            var cancelSent = false
            while (SystemClock.elapsedRealtime() < deadline) {
                if (cancelled() && !cancelSent) {
                    if (method == "steam.verify") throw java.io.InterruptedIOException("启动已取消")
                    cancelSent = true
                    request(context, "cancel")
                }
                val byte =
                    try {
                        input.read()
                    } catch (_: SocketTimeoutException) {
                        continue
                    }
                if (byte < 0) throw IOException("商店连接已关闭，请重试")
                if (byte != 10) {
                    partial.write(byte)
                    require(partial.size() <= responseLimit) { "运行时返回的数据过大" }
                    continue
                }
                val line = partial.toString("UTF-8")
                partial.reset()
                val response = JSONObject(line)
                if (response.optString("event") == "progress") {
                    progress(response.optString("message"), response.optInt("progress", -1))
                } else {
                    check(response.optBoolean("ok")) { response.optString("error", "商店操作失败") }
                    return response.opt("data") ?: true
                }
            }
            throw IOException("商店请求超时，请重试")
        }
    }
}
