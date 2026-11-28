package app.com.tmlpocket.feature.game

import java.io.File

internal object CrashDiagnostics {
    private val processHeader = Regex("""^-{3,} pid (\d+) at .*""")
    private val threadHeader = Regex("""^"([^"]+)".*""")
    private val threadtime = Regex("""^\S+\s+\S+\s+(\d+)\s+\d+\s+([VDIWEFA])\s+([^:]+?)\s*:\s?(.*)$""")
    private val important = Regex("fatal|stack overflow|unhandled exception|accessviolationexception|timed out|no focused window", RegexOption.IGNORE_CASE)

    fun anrThreads(trace: File, pid: Int): String {
        val threadNames = listOf("main", "SDLActivity", "Main Thread", "SDLThread", "RenderThread")
        val stacks = linkedMapOf<String, StringBuilder>()
        var target = true // Some devices omit the process delimiter.
        var current: StringBuilder? = null
        trace.bufferedReader().useLines { lines ->
            for (line in lines) {
                when {
                    line.startsWith("----- pid ") -> {
                        target = processHeader.matchEntire(line)?.groupValues?.get(1)?.toIntOrNull() == pid
                        current = null
                    }
                    line.startsWith("----- end ") -> { target = false; current = null }
                    !target -> continue
                    line.startsWith('"') -> {
                        val name = threadHeader.matchEntire(line)?.groupValues?.get(1).orEmpty()
                        current = if (name in threadNames &&
                            name !in stacks) StringBuilder().also { stacks[name] = it } else null
                        current?.appendLine(line.take(500))
                    }
                    line.isBlank() -> current = null
                    line.startsWith("  ") -> current?.let { if (it.length < 5000) it.appendLine(line.take(600)) }
                }
            }
        }
        return threadNames
            .mapNotNull { stacks[it]?.toString()?.trimEnd() }.joinToString("\n\n")
            .ifBlank { "系统 trace 中未找到该进程的主线程或渲染线程栈。" }
    }

    fun processLog(files: List<File>): String {
        val errors = ArrayDeque<String>()
        val recent = ArrayDeque<String>()
        for (file in files) file.useLines { lines ->
            for (line in lines) {
                val entry = line.take(1200)
                if ("error]" in entry || "[stderr]" in entry || important.containsMatchIn(entry)) {
                    if (errors.size == 32) errors.removeFirst()
                    errors.addLast(entry)
                }
                if (recent.size == 32) recent.removeFirst()
                recent.addLast(entry)
            }
        }
        return (errors.joinToString("\n").takeLast(10000) + "\n" +
            recent.filterNot { it in errors }.joinToString("\n").takeLast(6000)).trim()
    }

    fun gameLog(files: List<File>, pid: Int): String {
        val latest = ArrayDeque<String>()
        val failures = ArrayDeque<String>()
        for (file in files.filter { it.isFile }) file.useLines { lines ->
            for (line in lines) {
                val match = threadtime.matchEntire(line) ?: continue
                val (_, process, level, tag, message) = match.groupValues
                if (process.toIntOrNull() != pid) continue
                val entry = line.take(900)
                if (tag in setOf("DOTNET", "AndroidRuntime", "libc") && level in listOf("E", "F", "A") ||
                    important.containsMatchIn(message)) {
                    if (failures.size < 48) failures.addLast(entry)
                }
                if (tag in setOf("tMLPocketCoreClr", "tMLPocketStatus", "SDL", "DOTNET", "FNA3D", "app_process64", "AndroidRuntime") ||
                    level in listOf("E", "F", "A")) {
                    if ("fna-touch: polls=" in message) continue
                    if (latest.size == 40) latest.removeFirst()
                    latest.addLast(entry)
                }
            }
        }
        return buildString {
            if (failures.isNotEmpty()) appendLine(failures.joinToString("\n").take(10000))
            append(latest.filterNot { it in failures }.joinToString("\n").takeLast(11000))
        }.trim().ifEmpty { "未捕获到该游戏进程的诊断日志。" }
    }
}
