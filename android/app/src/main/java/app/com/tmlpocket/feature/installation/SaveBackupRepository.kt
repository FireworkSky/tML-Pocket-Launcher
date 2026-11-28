package app.com.tmlpocket.feature.installation

import android.system.ErrnoException
import android.system.Os
import android.system.OsConstants
import java.io.File
import java.io.FilterOutputStream
import java.io.InputStream
import java.io.OutputStream
import java.io.RandomAccessFile
import java.nio.channels.Channels
import java.nio.channels.FileChannel
import java.nio.file.Files
import java.nio.file.LinkOption.NOFOLLOW_LINKS
import java.nio.file.Path
import java.nio.file.StandardCopyOption.ATOMIC_MOVE
import java.nio.file.StandardOpenOption.CREATE
import java.nio.file.StandardOpenOption.CREATE_NEW
import java.nio.file.StandardOpenOption.READ
import java.nio.file.StandardOpenOption.WRITE
import java.nio.file.attribute.BasicFileAttributes
import java.text.Normalizer
import java.util.Locale
import java.util.UUID
import java.util.concurrent.CancellationException
import java.util.zip.CRC32
import java.util.zip.ZipEntry
import java.util.zip.ZipFile
import java.util.zip.ZipOutputStream
import org.json.JSONArray
import org.json.JSONObject

/**
 * Call off the main thread, with the game stopped (including during recovery). All extensions
 * below Players/ and Worlds/ are retained. Other userdata is never replaced.
 *
 * Repository calls are serialized across instances and processes. The on-disk journal is a
 * write-ahead undo log: an interrupted, uncommitted restore always returns to the old pair.
 * Readers outside this repository must call recover() before using saves after a restart;
 * two directory renames cannot provide a simultaneous switch to a running game.
 */
internal class SaveBackupRepository(private val userData: File, private val backupRoot: File) {
    fun list(): JSONArray = locked { data, backups ->
        recoverLocked(data, backups)
        JSONArray(archives(backups).sortedByDescending { Files.getLastModifiedTime(it, NOFOLLOW_LINKS).toMillis() }
            .map { metadata(it) })
    }

    fun hasSaves(): Boolean = locked { data, backups ->
        recoverLocked(data, backups)
        savesExist(data)
    }

    fun create(cancelled: () -> Boolean = { false }): JSONObject = locked { data, backups ->
        recoverLocked(data, backups)
        createLocked(data, backups, cancelled)
    }

    /** Validates every entry and its CRC before atomically publishing a private copy. */
    fun importArchive(source: File, cancelled: () -> Boolean = { false }): JSONObject = locked { data, backups ->
        recoverLocked(data, backups)
        checkCancelled(cancelled)
        checkBackup(archives(backups).size < MAX_BACKUPS) { "存档备份数量已达上限，请先将部分备份移到其他目录。" }
        val requested = source.absoluteFile.toPath().normalize()
        val input = requested.parent?.toFile()?.canonicalFile?.toPath()?.resolve(requested.fileName) ?: requested
        val before = regular(input)
        checkBackup(before.size() in 22..MAX_BYTES) { "备份文件超过 16 GiB 或不完整。" }
        val id = UUID.randomUUID().toString()
        val staged = backups.resolve(".backup-$id.tmp")
        val published = backups.resolve("$id.zip")
        try {
            FileChannel.open(staged, CREATE_NEW, WRITE, NOFOLLOW_LINKS).use { output ->
                val copied = Files.newInputStream(input, READ, NOFOLLOW_LINKS).use {
                    copy(it, Channels.newOutputStream(output), MAX_BYTES, cancelled, CRC32())
                }
                val after = regular(input)
                checkBackup(copied == before.size() && copied == after.size() &&
                    before.lastModifiedTime() == after.lastModifiedTime() && before.fileKey() == after.fileKey()) {
                    "导入时备份文件发生变化，请重试。"
                }
                output.force(true)
            }
            processArchive(staged, null, cancelled)
            checkCancelled(cancelled)
            move(staged, published)
            metadata(published)
        } finally {
            Files.deleteIfExists(staged)
        }
    }

    /** Fully validates the private archive. Callers must copy it, never move or modify it. */
    fun archive(id: String): File = locked { data, backups ->
        recoverLocked(data, backups)
        checkBackup(ID.matches(id)) { "存档备份编号无效。" }
        val file = backups.resolve("$id.zip")
        processArchive(file, null, { false })
        file.toFile()
    }

    fun delete(id: String) = locked { data, backups ->
        recoverLocked(data, backups)
        checkBackup(ID.matches(id)) { "存档备份编号无效。" }
        val archive = backups.resolve("$id.zip")
        regular(archive)
        Files.delete(archive)
        syncDirectory(backups)
    }

    /** Returns {id, restored:true, backup:<pre-restore backup entry or null>}. */
    fun restore(id: String, cancelled: () -> Boolean = { false }): JSONObject = locked { data, backups ->
        recoverLocked(data, backups)
        checkBackup(ID.matches(id)) { "存档备份编号无效。" }
        checkCancelled(cancelled)
        val archive = backups.resolve("$id.zip")
        regular(archive)
        val transaction = data.resolve(TRANSACTION)
        Files.createDirectory(transaction)
        syncDirectory(data)
        try {
            val incoming = Files.createDirectory(transaction.resolve("incoming"))
            val previous = Files.createDirectory(transaction.resolve("previous"))
            TREES.forEach { Files.createDirectory(incoming.resolve(it)) }
            processArchive(archive, incoming, cancelled)
            syncTreeDirectories(incoming)
            val safetyBackup = if (savesExist(data, cancelled)) createLocked(data, backups, cancelled) else null
            val journal = JSONObject().put("version", 1)
            TREES.forEach { name ->
                val live = data.resolve(name)
                if (exists(live)) directory(live)
                journal.put(name, exists(live))
            }
            writeMarker(transaction, "journal", journal.toString())
            checkCancelled(cancelled)
            for (name in TREES) {
                val live = data.resolve(name)
                if (journal.getBoolean(name)) move(live, previous.resolve(name))
                checkCancelled(cancelled)
                move(incoming.resolve(name), live)
                checkCancelled(cancelled)
            }
            // Once committed is durable, recovery keeps the entire new pair. Cancellation
            // after this point cannot undo a successful restore.
            writeMarker(transaction, "committed", "1")
            cleanupTransaction(transaction)
            JSONObject().put("id", id).put("restored", true).put("backup", safetyBackup ?: JSONObject.NULL)
        } catch (failure: Throwable) {
            try {
                recoverTransaction(data)
            } catch (recoveryFailure: Throwable) {
                failure.addSuppressed(recoveryFailure)
            }
            throw failure
        }
    }

    fun recover() {
        locked { data, backups -> recoverLocked(data, backups) }
    }

    private fun <T> locked(action: (Path, Path) -> T): T {
        try {
            return synchronized(MONITOR) {
                val data = root(userData)
                val backups = root(backupRoot)
                checkBackup(TREES.none { backups.startsWith(data.resolve(it)) } &&
                    !backups.startsWith(data.resolve(TRANSACTION))) { "备份目录不能放在存档或恢复工作目录内。" }
                val lock = data.resolve(".save-backup.lock")
                if (exists(lock)) regular(lock)
                FileChannel.open(lock, CREATE, WRITE, NOFOLLOW_LINKS).use { channel ->
                    channel.lock().use { action(data, backups) }
                }
            }
        } catch (failure: CancellationException) {
            if (failure.message == "存档操作已取消。") throw failure
            throw CancellationException("存档操作已取消。").also { it.initCause(failure) }
        } catch (failure: SaveBackupException) {
            throw failure
        } catch (failure: Exception) {
            throw SaveBackupException("存档操作失败，请检查备份文件、存储空间和文件访问权限。", failure)
        }
    }

    private fun root(file: File): Path {
        val requested = file.absoluteFile.toPath().normalize()
        // Canonicalize trusted ancestors (Android's /data/user/0 may itself be an alias),
        // but never accept a link as the configured directory or inside either root.
        val path = requested.parent?.toFile()?.canonicalFile?.toPath()?.resolve(requested.fileName) ?: requested
        if (exists(path)) directory(path)
        if (!exists(path)) Files.createDirectories(path)
        directory(path)
        return path
    }

    private fun recoverLocked(data: Path, backups: Path) {
        recoverTransaction(data)
        var scanned = 0
        Files.newDirectoryStream(backups).use { entries ->
            for (entry in entries) {
                checkBackup(++scanned <= MAX_DIRECTORY_ENTRIES) { "备份目录中的文件过多，请整理后重试。" }
                if (STAGING.matches(entry.fileName.toString())) {
                    regular(entry)
                    Files.delete(entry)
                    syncDirectory(backups)
                }
            }
        }
    }

    private fun recoverTransaction(data: Path) {
        val transaction = data.resolve(TRANSACTION)
        if (!exists(transaction)) return
        directory(transaction)
        val journalPath = transaction.resolve("journal")
        val committed = transaction.resolve("committed")
        if (exists(committed)) {
            checkBackup(readMarker(committed) == "1") { "存档恢复完成记录已损坏，请保留恢复工作目录。" }
        } else if (exists(journalPath)) {
            val journal = JSONObject(readMarker(journalPath))
            checkBackup(journal.length() == 3 && journal.getInt("version") == 1) { "存档恢复记录已损坏，请保留恢复工作目录。" }
            // Validate the whole journal before modifying either tree.
            TREES.forEach { checkBackup(journal.get(it) is Boolean) { "存档恢复记录已损坏，请保留恢复工作目录。" } }
            val previous = transaction.resolve("previous")
            directory(previous)
            for (name in TREES) {
                val old = previous.resolve(name)
                val live = data.resolve(name)
                if (exists(old)) {
                    checkBackup(journal.getBoolean(name)) { "存档恢复记录与原存档不一致，请保留恢复工作目录。" }
                    directory(old)
                    removeTree(live)
                    move(old, live)
                } else if (!journal.getBoolean(name)) {
                    removeTree(live)
                    syncDirectory(data)
                } else {
                    // Either its first rename never happened, or a previous recovery
                    // already put this original tree back. Both are safe to repeat.
                    directory(live)
                }
            }
        }
        cleanupTransaction(transaction)
    }

    private fun cleanupTransaction(transaction: Path) {
        removeTree(transaction.resolve("incoming"))
        val previous = transaction.resolve("previous")
        if (exists(previous)) {
            directory(previous)
            TREES.forEach { removeTree(previous.resolve(it)) }
            syncDirectory(previous)
        }
        // Keep committed until the undo journal is durably removed. Otherwise a death
        // during cleanup could interpret an already committed transaction as an undo.
        Files.deleteIfExists(transaction.resolve("journal"))
        syncDirectory(transaction)
        removeTree(previous)
        Files.deleteIfExists(transaction.resolve("committed"))
        Files.deleteIfExists(transaction.resolve("journal.tmp"))
        Files.deleteIfExists(transaction.resolve("committed.tmp"))
        syncDirectory(transaction)
        Files.delete(transaction)
        syncDirectory(transaction.parent)
    }

    private fun createLocked(data: Path, backups: Path, cancelled: () -> Boolean): JSONObject {
        checkCancelled(cancelled)
        checkBackup(archives(backups).size < MAX_BACKUPS) { "存档备份数量已达上限，请先将部分备份移到其他目录。" }
        val id = UUID.randomUUID().toString()
        val staged = backups.resolve(".backup-$id.tmp")
        val published = backups.resolve("$id.zip")
        var files = 0
        var bytes = 0L
        val names = Names()
        try {
            FileChannel.open(staged, CREATE_NEW, WRITE, NOFOLLOW_LINKS).use { channel ->
                val bounded = ArchiveOutput(Channels.newOutputStream(channel))
                ZipOutputStream(bounded).use { zip ->
                    for (name in TREES) {
                        val tree = data.resolve(name)
                        if (!exists(tree)) continue
                        walk(tree, name, 1, cancelled) { path, relative, attributes ->
                            val entryName = relative + if (attributes.isDirectory) "/" else ""
                            names.add(entryName)
                            val entry = ZipEntry(entryName)
                            entry.time = attributes.lastModifiedTime().toMillis()
                            zip.putNextEntry(entry)
                            if (!attributes.isDirectory) {
                                checkBackup(attributes.size() <= MAX_BYTES - bytes) { "存档总大小超过 16 GiB。" }
                                val crc = CRC32()
                                val copied = Files.newInputStream(path, READ, NOFOLLOW_LINKS).use {
                                    copy(it, zip, MAX_BYTES - bytes, cancelled, crc)
                                }
                                val after = regular(path)
                                checkBackup(copied == attributes.size() && copied == after.size() &&
                                    attributes.lastModifiedTime() == after.lastModifiedTime() &&
                                    attributes.fileKey() == after.fileKey()) { "备份时存档发生变化，请关闭游戏后重试。" }
                                bytes += copied
                                files++
                            }
                            zip.closeEntry()
                        }
                    }
                    checkBackup(files > 0) { "没有可备份的存档文件。" }
                    checkCancelled(cancelled)
                    zip.finish()
                    zip.flush()
                    channel.force(true)
                }
            }
            // Validate our own output with the same metadata limits used by restore.
            inspectDirectory(staged, cancelled)
            checkCancelled(cancelled)
            move(staged, published)
            return metadata(published)
        } finally {
            Files.deleteIfExists(staged)
        }
    }

    private fun savesExist(data: Path, cancelled: () -> Boolean = { false }): Boolean {
        var found = false
        val names = Names()
        for (name in TREES) {
            val tree = data.resolve(name)
            if (!exists(tree)) continue
            walk(tree, name, 1, cancelled) { _, relative, attributes ->
                names.add(relative + if (attributes.isDirectory) "/" else "")
                if (attributes.isRegularFile) found = true
            }
        }
        return found
    }

    private fun walk(
        path: Path,
        relative: String,
        depth: Int,
        cancelled: () -> Boolean,
        visit: (Path, String, BasicFileAttributes) -> Unit
    ) {
        checkCancelled(cancelled)
        checkBackup(depth <= MAX_DEPTH) { "存档目录层级超过限制。" }
        val attributes = safeAttributes(path)
        if (depth == 1) checkBackup(attributes.isDirectory) { "Players 和 Worlds 必须是目录。" }
        visit(path, relative, attributes)
        if (attributes.isDirectory) {
            Files.newDirectoryStream(path).use { entries ->
                for (entry in entries) walk(entry, "$relative/${entry.fileName}", depth + 1, cancelled, visit)
            }
        }
    }

    private fun processArchive(archive: Path, destination: Path?, cancelled: () -> Boolean) {
        val expectedEntries = inspectDirectory(archive, cancelled)
        val names = Names()
        var total = 0L
        var files = 0
        ZipFile(archive.toFile()).use { zip ->
            val entries = zip.entries()
            while (entries.hasMoreElements()) {
                checkCancelled(cancelled)
                val entry = entries.nextElement()
                val relative = names.add(entry.name)
                checkBackup(entry.method == ZipEntry.STORED || entry.method == ZipEntry.DEFLATED) { "备份使用了不支持的 ZIP 压缩方式。" }
                checkBackup(entry.size in 0..MAX_BYTES && entry.compressedSize in 0..MAX_BYTES) { "备份中的文件大小无效。" }
                val target = destination?.resolve(relative)
                checkBackup(target == null || target.normalize().startsWith(destination)) { "备份包含不安全的文件路径。" }
                if (entry.isDirectory) {
                    checkBackup(entry.size == 0L) { "备份中的目录条目包含文件数据。" }
                    if (target != null) Files.createDirectories(target)
                } else {
                    checkBackup(entry.size <= MAX_BYTES - total) { "备份解压后的总大小超过 16 GiB。" }
                    val crc = CRC32()
                    val copied = zip.getInputStream(entry).use { input ->
                        val limit = minOf(entry.size, MAX_BYTES - total)
                        if (target == null) {
                            copy(input, DISCARD, limit, cancelled, crc)
                        } else {
                            Files.createDirectories(target.parent)
                            FileChannel.open(target, CREATE_NEW, WRITE, NOFOLLOW_LINKS).use { output ->
                                val count = copy(input, Channels.newOutputStream(output), limit, cancelled, crc)
                                output.force(true)
                                count
                            }
                        }
                    }
                    checkBackup(copied == entry.size && crc.value == entry.crc) { "存档备份已损坏或不完整。" }
                    total += copied
                    files++
                }
            }
        }
        checkBackup(names.count == expectedEntries && files > 0) { "存档备份为空或目录记录不一致。" }
    }

    /** Bound and walk the central directory before ZipFile can allocate its entry table.
     * Also reject Unix links/special files and DOS reparse points, which ZipFile hides.
     * ZIP64 is needed for individual worlds/archives larger than 4 GiB.
     */
    private fun inspectDirectory(archive: Path, cancelled: () -> Boolean): Int {
        val length = regular(archive).size()
        checkBackup(length in 22..MAX_BYTES) { "备份文件超过 16 GiB 或不完整。" }
        RandomAccessFile(archive.toFile(), "r").use { input ->
            val tail = ByteArray(minOf(length, 65557L).toInt())
            input.seek(length - tail.size)
            input.readFully(tail)
            val end = (tail.size - 22 downTo 0).firstOrNull {
                uint(tail, it) == 0x06054b50L && it + 22 + ushort(tail, it + 20) == tail.size
            } ?: throw SaveBackupException("备份缺少 ZIP 结束记录。")
            val endOffset = length - tail.size + end
            checkBackup(ushort(tail, end + 4) == 0 && ushort(tail, end + 6) == 0 &&
                ushort(tail, end + 8) == ushort(tail, end + 10)) { "不支持分卷 ZIP 备份。" }
            var count = ushort(tail, end + 10).toLong()
            var size = uint(tail, end + 12)
            var offset = uint(tail, end + 16)
            var directoryEnd = endOffset
            if (count == 65535L || size == 0xffffffffL || offset == 0xffffffffL) {
                checkBackup(endOffset >= 20) { "备份缺少 ZIP64 定位记录。" }
                input.seek(endOffset - 20)
                val locator = ByteArray(20).also { input.readFully(it) }
                checkBackup(uint(locator, 0) == 0x07064b50L && uint(locator, 4) == 0L && uint(locator, 16) == 1L) {
                    "备份的 ZIP64 定位记录无效。"
                }
                val zip64Offset = ulong(locator, 8)
                checkBackup(zip64Offset in 0..(endOffset - 76)) { "备份的 ZIP64 位置无效。" }
                input.seek(zip64Offset)
                val header = ByteArray(56).also { input.readFully(it) }
                val recordSize = ulong(header, 4)
                checkBackup(uint(header, 0) == 0x06064b50L && recordSize in 44..4096 &&
                    zip64Offset + 12 + recordSize == endOffset - 20 &&
                    uint(header, 16) == 0L && uint(header, 20) == 0L &&
                    ulong(header, 24) == ulong(header, 32)) { "备份的 ZIP64 结束记录无效。" }
                count = ulong(header, 32)
                size = ulong(header, 40)
                offset = ulong(header, 48)
                directoryEnd = zip64Offset
            }
            checkBackup(count in 1..MAX_ENTRIES.toLong() && size in 46..MAX_METADATA &&
                offset >= 0 && offset <= directoryEnd && size == directoryEnd - offset) { "备份的目录信息超过限制或无效。" }
            input.seek(offset)
            val header = ByteArray(46)
            repeat(count.toInt()) {
                checkCancelled(cancelled)
                checkBackup(input.filePointer <= directoryEnd - header.size) { "备份的 ZIP 目录不完整。" }
                input.readFully(header)
                val mode = (uint(header, 38) ushr 16).toInt() and 0xf000
                checkBackup(uint(header, 0) == 0x02014b50L && ushort(header, 34) == 0 &&
                    ushort(header, 8) and 1 == 0 && mode in listOf(0, 0x8000, 0x4000) &&
                    uint(header, 38) and 0x400L == 0L) { "备份包含链接、特殊文件、加密或分卷条目。" }
                val nameLength = ushort(header, 28)
                checkBackup(nameLength in 1..MAX_PATH_BYTES) { "备份中的文件路径过长。" }
                val next = input.filePointer + nameLength + ushort(header, 30) + ushort(header, 32)
                checkBackup(next <= directoryEnd) { "备份的目录信息不完整。" }
                input.seek(next)
            }
            checkBackup(input.filePointer == directoryEnd) { "备份的 ZIP 目录记录不一致。" }
            return count.toInt()
        }
    }

    private fun archives(backups: Path): List<Path> {
        val result = ArrayList<Path>()
        var scanned = 0
        Files.newDirectoryStream(backups).use { entries ->
            for (entry in entries) {
                checkBackup(++scanned <= MAX_DIRECTORY_ENTRIES) { "备份目录中的文件过多，请整理后重试。" }
                if (!ARCHIVE.matches(entry.fileName.toString())) continue
                checkBackup(regular(entry).size() in 22..MAX_BYTES) { "存档备份文件大小无效或超过 16 GiB。" }
                checkBackup(result.size < MAX_BACKUPS) { "存档备份数量超过限制。" }
                result.add(entry)
            }
        }
        return result
    }

    private fun metadata(path: Path): JSONObject {
        val attributes = regular(path)
        val id = path.fileName.toString().removeSuffix(".zip")
        return JSONObject().put("id", id).put("name", "存档备份 $id")
            .put("created", attributes.lastModifiedTime().toMillis()).put("size", attributes.size())
    }

    private fun writeMarker(transaction: Path, name: String, content: String) {
        val temporary = transaction.resolve("$name.tmp")
        FileChannel.open(temporary, CREATE_NEW, WRITE, NOFOLLOW_LINKS).use { channel ->
            val buffer = java.nio.ByteBuffer.wrap(content.toByteArray(Charsets.UTF_8))
            while (buffer.hasRemaining()) channel.write(buffer)
            channel.force(true)
        }
        move(temporary, transaction.resolve(name))
    }

    private fun readMarker(path: Path): String {
        checkBackup(regular(path).size() in 1..MAX_MARKER_BYTES.toLong()) { "存档恢复记录大小无效。" }
        return Files.newInputStream(path, READ, NOFOLLOW_LINKS).use { input ->
            val bytes = ByteArray(MAX_MARKER_BYTES + 1)
            var size = 0
            while (size < bytes.size) {
                val count = input.read(bytes, size, bytes.size - size)
                if (count < 0) break
                size += count
            }
            checkBackup(size in 1..MAX_MARKER_BYTES) { "存档恢复记录大小无效。" }
            String(bytes, 0, size, Charsets.UTF_8)
        }
    }

    private fun move(source: Path, target: Path) {
        checkBackup(!exists(target)) { "存档目标已存在，请恢复未完成的操作后重试。" }
        Files.move(source, target, ATOMIC_MOVE)
        syncDirectory(target.parent)
        if (source.parent != target.parent) syncDirectory(source.parent)
    }

    private fun syncTreeDirectories(path: Path) {
        directory(path)
        Files.newDirectoryStream(path).use { entries ->
            for (entry in entries) if (safeAttributes(entry).isDirectory) syncTreeDirectories(entry)
        }
        syncDirectory(path)
    }

    private fun syncDirectory(path: Path) {
        directory(path)
        val descriptor = Os.open(path.toString(), OsConstants.O_RDONLY or OsConstants.O_NOFOLLOW, 0)
        try {
            Os.fsync(descriptor)
        } catch (failure: ErrnoException) {
            // Some shared Android volumes cannot fsync directories.
            if (failure.errno !in setOf(OsConstants.EINVAL, OsConstants.EOPNOTSUPP, OsConstants.ENOSYS)) throw failure
        } finally {
            Os.close(descriptor)
        }
    }

    /** Never follows links, including during rollback/cleanup. Limit damage from corrupt staging. */
    private fun removeTree(path: Path) {
        var remaining = MAX_ENTRIES * 3 + 32
        fun remove(current: Path, depth: Int) {
            if (!exists(current)) return
            checkBackup(--remaining >= 0 && depth <= MAX_DEPTH + 4) { "恢复工作目录超过清理限制，请保留目录并检查。" }
            val attributes = Files.readAttributes(current, BasicFileAttributes::class.java, NOFOLLOW_LINKS)
            if (attributes.isDirectory && !attributes.isSymbolicLink &&
                current.toRealPath() == current.toAbsolutePath().normalize()) {
                Files.newDirectoryStream(current).use { entries ->
                    for (entry in entries) remove(entry, depth + 1)
                }
            }
            Files.delete(current)
        }
        remove(path, 0)
    }

    private fun safeAttributes(path: Path): BasicFileAttributes {
        val attributes = Files.readAttributes(path, BasicFileAttributes::class.java, NOFOLLOW_LINKS)
        checkBackup(!attributes.isSymbolicLink && (attributes.isRegularFile || attributes.isDirectory) &&
            path.toRealPath() == path.toAbsolutePath().normalize()) { "存档存储中包含链接或特殊文件。" }
        return attributes
    }

    private fun regular(path: Path): BasicFileAttributes = safeAttributes(path).also {
        checkBackup(it.isRegularFile) { "存档备份文件不存在或不是普通文件。" }
    }

    private fun directory(path: Path) {
        checkBackup(safeAttributes(path).isDirectory) { "存档存储目录不存在或无效。" }
    }

    private fun exists(path: Path): Boolean = Files.exists(path, NOFOLLOW_LINKS)

    private fun checkCancelled(cancelled: () -> Boolean) {
        if (cancelled() || Thread.currentThread().isInterrupted) throw CancellationException("存档操作已取消。")
    }

    private fun copy(input: InputStream, output: OutputStream, limit: Long, cancelled: () -> Boolean, crc: CRC32): Long {
        val buffer = ByteArray(64 * 1024)
        var total = 0L
        while (true) {
            checkCancelled(cancelled)
            val count = input.read(buffer)
            if (count < 0) return total
            checkBackup(count.toLong() <= limit - total) { "存档备份中的数据超过大小限制。" }
            output.write(buffer, 0, count)
            crc.update(buffer, 0, count)
            total += count
        }
    }

    private class ArchiveOutput(output: OutputStream) : FilterOutputStream(output) {
        private var size = 0L
        override fun write(value: Int) {
            checkBackup(size < MAX_BYTES) { "备份文件超过 16 GiB。" }
            out.write(value)
            size++
        }
        override fun write(bytes: ByteArray, offset: Int, length: Int) {
            checkBackup(length.toLong() <= MAX_BYTES - size) { "备份文件超过 16 GiB。" }
            out.write(bytes, offset, length)
            size += length
        }
    }

    private class SaveBackupException(message: String, cause: Throwable? = null) : IllegalStateException(message, cause)

    /** Also rejects case/Unicode aliases and file-vs-directory collisions on portable storage. */
    private class Names {
        private val explicit = HashSet<String>()
        private val types = HashMap<String, Boolean>()
        private val spellings = HashMap<String, String>()
        private var metadataBytes = 0L
        var count = 0
            private set

        fun add(name: String): String {
            val nameBytes = name.toByteArray(Charsets.UTF_8).size
            metadataBytes += 128 + nameBytes
            checkBackup(++count <= MAX_ENTRIES && metadataBytes <= MAX_METADATA && nameBytes <= MAX_PATH_BYTES &&
                name.isNotEmpty() && !name.startsWith('/') && '\\' !in name && ':' !in name &&
                name.none { it.code < 32 || it.code == 127 || it in "<>\"|?*" }) { "存档路径不安全或数量、长度超过限制。" }
            val isDirectory = name.endsWith('/')
            val relative = if (isDirectory) name.dropLast(1) else name
            val parts = relative.split('/')
            checkBackup(parts.size in 1..MAX_DEPTH && parts.first() in TREES &&
                (parts.size > 1 || isDirectory) && parts.all {
                    it.isNotEmpty() && it != "." && it != ".." && !it.endsWith('.') && !it.endsWith(' ') &&
                        it.toByteArray(Charsets.UTF_8).size <= 255 &&
                        it.substringBefore('.').uppercase(Locale.ROOT) !in RESERVED_NAMES
                }) { "存档路径不安全。" }
            var original = ""
            for ((index, part) in parts.withIndex()) {
                original = if (original.isEmpty()) part else "$original/$part"
                val key = Normalizer.normalize(original, Normalizer.Form.NFC).lowercase(Locale.ROOT)
                val directory = index < parts.lastIndex || isDirectory
                val oldSpelling = spellings.putIfAbsent(key, original)
                checkBackup(oldSpelling == null || oldSpelling == original) { "存档包含大小写或文字规范化后重名的路径。" }
                val oldType = types.putIfAbsent(key, directory)
                checkBackup(types.size <= MAX_ENTRIES) { "存档文件和目录数量超过限制。" }
                checkBackup(oldType == null || oldType == directory) { "存档备份中的文件与目录路径冲突。" }
                if (index == parts.lastIndex) checkBackup(explicit.add(key)) { "存档备份中存在重复路径。" }
            }
            return relative
        }
    }

    private companion object {
        val MONITOR = Any()
        val DISCARD = object : OutputStream() {
            override fun write(value: Int) = Unit
            override fun write(bytes: ByteArray, offset: Int, length: Int) = Unit
        }
        val TREES = listOf("Players", "Worlds")
        const val TRANSACTION = ".save-backup-restore"
        const val MAX_BYTES = 16L * 1024 * 1024 * 1024
        const val MAX_METADATA = 32L * 1024 * 1024
        const val MAX_ENTRIES = 100_000
        const val MAX_BACKUPS = 10_000
        const val MAX_DIRECTORY_ENTRIES = 20_000
        const val MAX_PATH_BYTES = 1024
        const val MAX_DEPTH = 64
        const val MAX_MARKER_BYTES = 1024
        val ID = Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
        val ARCHIVE = Regex("${ID.pattern}\\.zip")
        val STAGING = Regex("\\.backup-${ID.pattern}\\.tmp")
        val RESERVED_NAMES = setOf("CON", "PRN", "AUX", "NUL") + (1..9).flatMap { listOf("COM$it", "LPT$it") }

        fun checkBackup(condition: Boolean, message: () -> String) {
            if (!condition) throw SaveBackupException(message())
        }

        fun ushort(bytes: ByteArray, offset: Int): Int =
            (bytes[offset].toInt() and 255) or ((bytes[offset + 1].toInt() and 255) shl 8)

        fun uint(bytes: ByteArray, offset: Int): Long =
            ushort(bytes, offset).toLong() or (ushort(bytes, offset + 2).toLong() shl 16)

        fun ulong(bytes: ByteArray, offset: Int): Long =
            (uint(bytes, offset) or (uint(bytes, offset + 4) shl 32)).also {
                checkBackup(it >= 0) { "备份的 ZIP64 数值超过限制。" }
            }
    }
}
