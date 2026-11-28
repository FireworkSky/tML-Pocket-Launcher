package app.com.tmlpocket.feature.game

import android.app.ActivityManager
import android.app.ApplicationExitInfo
import android.content.ClipData
import android.content.Intent
import android.content.Context
import android.net.Uri
import app.com.tmlpocket.feature.installation.GameStorage
import android.os.Build
import android.os.Process
import android.util.AtomicFile
import android.util.Log
import java.io.File
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream
import java.util.UUID
import org.json.JSONObject

/** The launcher reads this journal after :game dies; no fatal-signal handler is replaced. */
internal object GameCrashReports {
    private const val TAG = "tMLPocketLauncher"
    private const val LIMIT = 512 * 1024
    private var capture: java.lang.Process? = null
    private var capturedSession: String? = null

    data class Report(val session: String, val text: String)

    private fun root(context: Context) = File(context.filesDir, "userdata/Logs/game-crashes")
    private fun directory(context: Context, session: String): File {
        require(session.matches(Regex("[a-f0-9-]{36}")))
        return File(root(context), session)
    }

    private fun write(file: File, text: String) {
        file.parentFile?.mkdirs()
        val atomic = AtomicFile(file)
        val stream = atomic.startWrite()
        try {
            stream.write(text.toByteArray(Charsets.UTF_8))
            atomic.finishWrite(stream)
        } catch (failure: Throwable) {
            atomic.failWrite(stream)
            throw failure
        }
    }

    private fun read(file: File): String = runCatching {
        AtomicFile(file).openRead().bufferedReader().use { it.readText() }
    }.getOrDefault("")

    fun begin(context: Context, version: String, renderer: String): String {
        stopCapture()
        val session = UUID.randomUUID().toString()
        val folder = directory(context, session).apply { mkdirs() }
        write(File(folder, "session.json"), JSONObject()
            .put("started", System.currentTimeMillis()).put("version", version)
            .put("launcher", "${app.com.tmlpocket.BuildConfig.VERSION_NAME} (${app.com.tmlpocket.BuildConfig.VERSION_CODE})")
            .put("device", "${Build.MANUFACTURER} ${Build.MODEL} · Android ${Build.VERSION.RELEASE} (API ${Build.VERSION.SDK_INT})")
            .put("renderer", renderer).toString())
        write(File(root(context), "current"), session)
        // Bound retained data. Never touch game worlds, players or other log directories.
        root(context).listFiles()?.filter { it.isDirectory && it.name != session }
            ?.sortedByDescending { it.lastModified() }?.drop(4)?.forEach { it.deleteRecursively() }
        startCapture(context, session, folder)
        return session
    }

    private fun startCapture(context: Context, session: String, folder: File) {
        runCatching {
            val process = ProcessBuilder("logcat", "-b", "main", "-b", "crash", "-v", "threadtime",
                "--uid=${context.applicationInfo.uid}", "-T", "1")
                .redirectErrorStream(true).start()
            capture = process
            capturedSession = session
            Thread({
                try {
                    process.inputStream.bufferedReader().use { input ->
                        var file = File(folder, "logcat.log")
                        var output = file.outputStream()
                        var size = 0
                        try {
                            input.forEachLine { line ->
                                val bytes = (line.take(16000) + "\n").toByteArray(Charsets.UTF_8)
                                if (size + bytes.size > LIMIT) {
                                    output.close()
                                    val previous = File(folder, "logcat-previous.log")
                                    previous.delete()
                                    check(file.renameTo(previous))
                                    output = file.outputStream()
                                    size = 0
                                }
                                output.write(bytes)
                                output.flush()
                                size += bytes.size
                            }
                        } finally { output.close() }
                    }
                } catch (failure: Exception) {
                    Log.w(TAG, "Game log capture ended: ${failure.javaClass.simpleName}")
                }
            }, "GameLogCapture").apply { isDaemon = true; start() }
        }.onFailure { Log.w(TAG, "Game log capture unavailable", it) }
    }

    private fun stopCapture() {
        capture?.destroy()
        capture = null
        capturedSession = null
    }

    fun started(context: Context, session: String) {
        write(File(directory(context, session), "process.json"), JSONObject()
            .put("pid", Process.myPid()).put("started", System.currentTimeMillis()).toString())
        stage(context, session, "正在加载 SDL、FNA3D 和原生运行库")
    }

    fun startGameLog(context: Context, session: String) {
        val file = File(directory(context, session), "game-process.log")
        if (!app.com.tmlpocket.core.platform.NativeCoreClr.nativeStartGameLog(file.absolutePath))
            Log.w(TAG, "Could not open game process log")
    }

    fun stage(context: Context, session: String, detail: String) {
        runCatching { write(File(directory(context, session), "stage"), detail.take(2000)) }
    }

    fun firstFrame(context: Context, session: String) {
        runCatching { write(File(directory(context, session), "first-frame"), "presented") }
    }

    fun failed(context: Context, session: String, failure: Throwable) {
        runCatching {
            write(File(directory(context, session), "exception.log"), failure.stackTraceToString().take(64000))
        }.onFailure { Log.e(TAG, "Could not save game exception", it) }
    }

    fun normalExit(context: Context, session: String) {
        runCatching { write(File(directory(context, session), "normal-exit"), "returned") }
    }

    fun handled(context: Context, session: String) {
        write(File(directory(context, session), "handled"), "shown")
    }

    /** Called on a launcher worker, including after the launcher itself was recreated. */
    fun collect(context: Context): Report? {
        val session = read(File(root(context), "current")).trim()
        if (session.isBlank()) return null
        val folder = directory(context, session)
        if (File(folder, "handled").exists()) return null
        val metadata = JSONObject(read(File(folder, "session.json")))
        val manager = context.getSystemService(Context.ACTIVITY_SERVICE) as ActivityManager
        if (manager.runningAppProcesses?.any { it.processName == "${context.packageName}:game" } == true)
            return null
        // Give ActivityManager time to publish exit info and avoid racing a new Activity launch.
        if (System.currentTimeMillis() - metadata.getLong("started") < 3000) return null
        val process = read(File(folder, "process.json")).takeIf { it.isNotBlank() }?.let(::JSONObject)
        val pid = process?.optInt("pid") ?: 0
        val exit = if (Build.VERSION.SDK_INT >= 30) runCatching {
            manager.getHistoricalProcessExitReasons(context.packageName, pid, 16).firstOrNull {
                it.processName == "${context.packageName}:game" &&
                    it.timestamp >= metadata.getLong("started") && (pid == 0 || it.pid == pid)
            }
        }.onFailure { Log.w(TAG, "System exit history unavailable", it) }.getOrNull() else null
        val exception = read(File(folder, "exception.log"))
        val systemCrash = Build.VERSION.SDK_INT >= 30 && exit != null &&
            (exit.reason in setOf(ApplicationExitInfo.REASON_CRASH,
                ApplicationExitInfo.REASON_CRASH_NATIVE, ApplicationExitInfo.REASON_ANR) ||
                (exit.reason == ApplicationExitInfo.REASON_SIGNALED && exit.status != 9))
        val expectedStop = Build.VERSION.SDK_INT >= 30 && when (exit?.reason) {
            ApplicationExitInfo.REASON_USER_REQUESTED,
            ApplicationExitInfo.REASON_PACKAGE_UPDATED -> true
            else -> false
        }
        if (exception.isBlank() && !systemCrash &&
            (expectedStop || File(folder, "normal-exit").exists())) {
            handled(context, session)
            if (capturedSession == session) stopCapture()
            return null
        }
        val gamePid = if (Build.VERSION.SDK_INT >= 30 && exit != null) exit.pid else pid
        val summary = buildString {
            appendLine(if (File(folder, "first-frame").exists()) "游戏进程异常退出" else "游戏启动失败：进程在首帧前退出")
            appendLine("tML: ${metadata.optString("version")}；渲染器: ${metadata.optString("renderer")}")
            if (metadata.has("device")) appendLine("设备: ${metadata.getString("device")}；启动器: ${metadata.optString("launcher")}")
            appendLine("PID: $gamePid；启动时间: ${java.util.Date(metadata.getLong("started"))}")
            appendLine("最后阶段: ${read(File(folder, "stage")).ifBlank { "正在创建游戏进程" }}")
            if (Build.VERSION.SDK_INT >= 30 && exit != null) {
                appendLine("退出时间: ${java.util.Date(exit.timestamp)}")
                val reason = when (exit.reason) {
                    ApplicationExitInfo.REASON_CRASH_NATIVE -> "Native 崩溃（C/C++）"
                    ApplicationExitInfo.REASON_CRASH -> "Java 未捕获异常"
                    ApplicationExitInfo.REASON_SIGNALED -> "被信号终止"
                    ApplicationExitInfo.REASON_LOW_MEMORY -> "系统内存不足"
                    ApplicationExitInfo.REASON_ANR -> "游戏无响应（ANR）"
                    ApplicationExitInfo.REASON_EXIT_SELF -> "进程自行退出"
                    else -> "系统退出原因 ${exit.reason}"
                }
                appendLine("$reason；状态/信号: ${exit.status}")
                if (exit.reason == ApplicationExitInfo.REASON_LOW_MEMORY) {
                    if (exit.pss > 0) appendLine("系统最近采样的进程 PSS: ${exit.pss / 1024} MiB")
                    if (exit.rss > 0) appendLine("系统最近采样的进程 RSS: ${exit.rss / 1024} MiB")
                }
                if (exit.reason in setOf(ApplicationExitInfo.REASON_SIGNALED, ApplicationExitInfo.REASON_CRASH_NATIVE)) {
                    val signal = when (exit.status) {
                        4 -> "SIGILL"
                        6 -> "SIGABRT"
                        7 -> "SIGBUS"
                        8 -> "SIGFPE"
                        9 -> "SIGKILL"
                        11 -> "SIGSEGV"
                        else -> "signal ${exit.status}"
                    }
                    appendLine("信号: $signal")
                }
                appendLine(exit.description.orEmpty())
                runCatching {
                    val trace = File(folder, "system-exit-trace.bin")
                    val input = exit.traceInputStream
                    if (input == null) appendLine("系统没有提供崩溃 trace；以下保留进程日志。")
                    input?.use {
                        trace.outputStream().use { output ->
                            val buffer = ByteArray(8192)
                            var remaining = 4 * 1024 * 1024
                            while (remaining > 0) {
                                val count = input.read(buffer, 0, minOf(buffer.size, remaining))
                                if (count < 0) break
                                output.write(buffer, 0, count)
                                remaining -= count
                            }
                            if (remaining == 0) appendLine("系统 trace 达到 4 MiB 保存上限，文件可能被截断。")
                        }
                        appendLine("系统崩溃 trace 已保存为 system-exit-trace.bin。")
                        if (exit.reason == ApplicationExitInfo.REASON_CRASH_NATIVE)
                            appendLine(NativeTombstone.describe(trace.readBytes()))
                        else if (exit.reason == ApplicationExitInfo.REASON_ANR) {
                            appendLine("\n--- ANR 线程栈 ---")
                            appendLine(CrashDiagnostics.anrThreads(trace, gamePid))
                        }
                    }
                }.onFailure { appendLine("系统未允许读取崩溃 trace: ${it.javaClass.simpleName}") }
            } else {
                appendLine("系统未提供退出记录；无法确定是 native 崩溃、强制结束还是内存回收。")
            }
            if (exception.isNotBlank()) appendLine("\n$exception")
            val directLogs = listOf("game-process.log.1", "game-process.log").map { File(folder, it) }.filter { it.isFile }
            if (directLogs.isNotEmpty()) {
                appendLine("\n--- 游戏进程直接记录 ---")
                appendLine(CrashDiagnostics.processLog(directLogs))
            }
            appendLine("\n--- 游戏进程异常与近期日志 ---")
            appendLine(CrashDiagnostics.gameLog(listOf(File(folder, "logcat-previous.log"), File(folder, "logcat.log")), gamePid))
            appendLine("\n报告目录: ${folder.absolutePath}")
        }
        write(File(folder, "report.log"), summary)
        if (capturedSession == session) stopCapture()
        Log.e(TAG, "Game process exited unexpectedly; pid=$pid; report=${folder.absolutePath}")
        return Report(session, summary)
    }

    fun mostRecentReport(context: Context): String =
        root(context).listFiles().orEmpty().filter { File(it, "report.log").isFile }
            .maxByOrNull { File(it, "report.log").lastModified() }?.name ?: error("没有已保存的崩溃报告")

    fun archive(context: Context, session: String): android.net.Uri {
        val folder = directory(context, session)
        check(File(folder, "report.log").isFile)
        val file = File(folder, "tML-crash-$session.zip")
        if (!file.exists()) {
            val staging = File.createTempFile("report-", ".tmp", folder)
            try {
                ZipOutputStream(staging.outputStream().buffered()).use { zip ->
                    writeSessionLogs(zip, folder)
                }
                check(staging.renameTo(file))
            } finally { staging.delete() }
        }
        return androidx.core.content.FileProvider.getUriForFile(context, "${context.packageName}.controlpacks", file)
    }

    fun readLogs(context: Context): String {
        val gameStorage = GameStorage(context)
        val gameRoot = gameStorage.packageRoot
        val roots =
            listOfNotNull(File(context.filesDir, "userdata"), runCatching { gameStorage.userData() }.getOrNull()).distinct() +
                File(gameRoot, "games").listFiles().orEmpty().filter {
                    it.isDirectory && (it.name == "tmodloader" || it.name.startsWith("tmodloader-"))
                }
        val logs =
            roots.flatMap { root ->
                root
                    .walkTopDown()
                    .maxDepth(5)
                    .filter { it.isFile && it.name.endsWith(".log") }
                    .take(15)
                    .toList()
            }
        val crashReports = File(context.filesDir, "userdata/Logs/game-crashes").listFiles()
            ?.filter { it.isDirectory }
            ?.map { File(it, "report.log") }
            ?.filter { it.isFile }.orEmpty()
        return (logs + crashReports).distinct()
            .sortedByDescending { it.lastModified() }
            .take(5)
            .mapNotNull { file ->
                // The game may rotate a log between listing it and opening it.
                try {
                    java.io.RandomAccessFile(file, "r").use { input ->
                        val end = input.length()
                        val size = minOf(end, 30_000L).toInt()
                        input.seek(end - size)
                        val bytes = ByteArray(size)
                        val count = input.read(bytes).coerceAtLeast(0)
                        "--- ${file.name} ---\n" + String(bytes, 0, count, Charsets.UTF_8)
                    }
                } catch (_: java.io.IOException) { null }
            }
            .joinToString("\n\n")
            .ifBlank { "尚无启动日志。导入资源并启动游戏后可在这里查看。" }
    }

    fun shareIntent(uri: Uri, title: String): Intent {
        val send = Intent(Intent.ACTION_SEND).setType("application/zip")
            .putExtra(Intent.EXTRA_STREAM, uri).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        send.clipData = ClipData.newRawUri(null, uri)
        return Intent.createChooser(send, title)
    }

    fun shareRun(context: Context): Uri {
        val current = read(File(root(context), "current")).trim()
        val folder = current.takeIf { it.isNotEmpty() }?.let { directory(context, it) }
        val cache = File(context.cacheDir, "game-diagnostics").apply { mkdirs() }
        val file = archiveRun(folder, readLogs(context), cache)
        return androidx.core.content.FileProvider.getUriForFile(context, "${context.packageName}.controlpacks", file)
    }

    internal fun archiveRun(folder: File?, displayedLogs: String, cache: File): File {
        val file = File.createTempFile("tML-game-", ".zip", cache)
        try {
            ZipOutputStream(file.outputStream().buffered()).use { zip ->
                zip.putNextEntry(ZipEntry("recent-logs.txt"))
                zip.write(displayedLogs.toByteArray(Charsets.UTF_8))
                zip.closeEntry()
                if (folder != null) writeSessionLogs(zip, folder)
            }
        } catch (failure: Throwable) {
            file.delete()
            throw failure
        }
        cache.listFiles().orEmpty().filter { it != file && it.extension == "zip" }
            .sortedByDescending { it.lastModified() }.drop(3).forEach { it.delete() }
        return file
    }

    private fun writeSessionLogs(zip: ZipOutputStream, folder: File) {
        for (name in listOf("report.log", "session.json", "process.json", "stage", "first-frame", "normal-exit",
            "exception.log", "game-process.log.1", "game-process.log", "logcat-previous.log", "logcat.log", "system-exit-trace.bin")) {
            // An active session can append or rotate its logs during export.
            val input = try { File(folder, name).inputStream() } catch (_: java.io.FileNotFoundException) { continue }
            input.use {
                var remaining = it.channel.size()
                zip.putNextEntry(ZipEntry(name))
                val buffer = ByteArray(DEFAULT_BUFFER_SIZE)
                while (remaining > 0) {
                    val count = it.read(buffer, 0, minOf(buffer.size.toLong(), remaining).toInt())
                    if (count <= 0) break
                    zip.write(buffer, 0, count)
                    remaining -= count
                }
                zip.closeEntry()
            }
        }
    }
}
