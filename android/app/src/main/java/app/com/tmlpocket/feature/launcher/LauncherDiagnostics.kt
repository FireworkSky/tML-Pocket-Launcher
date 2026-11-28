package app.com.tmlpocket.feature.launcher

import android.app.ActivityManager
import android.app.ApplicationExitInfo
import android.content.Context
import android.net.Uri
import android.os.Build
import android.os.Process
import android.util.AtomicFile
import android.webkit.WebView
import androidx.core.content.FileProvider
import app.com.tmlpocket.BuildConfig
import java.io.File
import java.time.Instant
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream
import org.json.JSONObject

internal object LauncherDiagnostics {
    private fun journal(context: Context) = AtomicFile(File(context.filesDir, "launcher-exception.json"))

    @Synchronized
    fun record(context: Context, thread: Thread, error: Throwable) {
        val data = JSONObject()
            .put("time", System.currentTimeMillis()).put("pid", Process.myPid())
            .put("version", BuildConfig.VERSION_NAME).put("thread", thread.name)
            .put("stack", error.stackTraceToString().take(96 * 1024))
        val file = journal(context)
        val stream = file.startWrite()
        try {
            stream.write(data.toString().toByteArray())
            file.finishWrite(stream)
        } catch (failure: Throwable) {
            file.failWrite(stream)
            throw failure
        }
    }

    fun archive(context: Context): Uri {
        val saved = runCatching { JSONObject(journal(context).readFully().toString(Charsets.UTF_8)) }.getOrNull()
        val folder = File(context.cacheDir, "launcher-diagnostics").apply { mkdirs() }
        val time = System.currentTimeMillis()
        val output = File.createTempFile("tML-launcher-${time}-", ".zip", folder)
        try {
            ZipOutputStream(output.outputStream().buffered()).use { zip ->
                val report = StringBuilder()
                report.appendLine("tML Pocket Launcher ${BuildConfig.VERSION_NAME} (${BuildConfig.VERSION_CODE})")
                report.appendLine("Exported: ${Instant.ofEpochMilli(time)}")
                report.appendLine("Device: ${Build.MANUFACTURER} ${Build.MODEL}, Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})")
                report.appendLine("WebView: ${WebView.getCurrentWebViewPackage()?.versionName ?: "unknown"}")
                var includeSaved = saved != null
                var hasExit = false
                if (Build.VERSION.SDK_INT >= 30) {
                    val exit = runCatching {
                        context.getSystemService(ActivityManager::class.java)
                            .getHistoricalProcessExitReasons(context.packageName, 0, 0)
                            .firstOrNull { it.processName == context.packageName && it.reason in setOf(
                                ApplicationExitInfo.REASON_CRASH, ApplicationExitInfo.REASON_CRASH_NATIVE,
                                ApplicationExitInfo.REASON_ANR, ApplicationExitInfo.REASON_INITIALIZATION_FAILURE) }
                    }.onFailure { report.appendLine("Exit history unavailable: ${it.javaClass.simpleName}") }.getOrNull()
                    if (exit != null) {
                        val matches = saved != null && saved.optInt("pid") == exit.pid &&
                            kotlin.math.abs(saved.optLong("time") - exit.timestamp) < 60_000
                        if (matches || exit.timestamp >= (saved?.optLong("time") ?: 0)) {
                            hasExit = true
                            includeSaved = matches
                            report.appendLine("Launcher exit: ${Instant.ofEpochMilli(exit.timestamp)}, pid=${exit.pid}, reason=${exit.reason}, status=${exit.status}")
                            report.appendLine(exit.description)
                            runCatching {
                                exit.traceInputStream?.use { input ->
                                    zip.putNextEntry(ZipEntry("system-exit-trace.bin"))
                                    try {
                                        val buffer = ByteArray(8192)
                                        var remaining = 4 * 1024 * 1024
                                        while (remaining > 0) {
                                            val count = input.read(buffer, 0, minOf(buffer.size, remaining))
                                            if (count <= 0) break
                                            zip.write(buffer, 0, count)
                                            remaining -= count
                                        }
                                        if (remaining == 0) report.appendLine("Trace limited to 4 MiB.")
                                    } finally { zip.closeEntry() }
                                } ?: report.appendLine("No system trace available.")
                            }.onFailure { report.appendLine("Trace unavailable: ${it.javaClass.simpleName}") }
                        }
                    }
                }
                if (includeSaved && saved != null) {
                    report.appendLine("Uncaught launcher exception: ${Instant.ofEpochMilli(saved.getLong("time"))}")
                    report.appendLine("Version=${saved.optString("version")}, pid=${saved.optInt("pid")}, thread=${saved.optString("thread")}")
                    report.appendLine(saved.optString("stack"))
                } else if (!hasExit) {
                    report.appendLine("Current diagnostic snapshot. No launcher crash record is available.")
                }
                zip.putNextEntry(ZipEntry("report.txt"))
                zip.write(report.toString().toByteArray())
                zip.closeEntry()
            }
        } catch (failure: Throwable) {
            output.delete()
            throw failure
        }
        folder.listFiles().orEmpty().filter { it != output && it.extension == "zip" }
            .sortedByDescending { it.lastModified() }.drop(3).forEach { it.delete() }
        return FileProvider.getUriForFile(context, "${context.packageName}.controlpacks", output)
    }
}
