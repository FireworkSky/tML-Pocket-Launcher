package app.com.tmlpocket.feature.files

import android.database.MatrixCursor
import android.os.CancellationSignal
import android.os.ParcelFileDescriptor
import android.provider.DocumentsContract
import android.provider.DocumentsContract.Document
import android.provider.DocumentsContract.Root
import android.provider.DocumentsProvider
import android.webkit.MimeTypeMap
import app.com.tmlpocket.R
import app.com.tmlpocket.feature.installation.GameStorage
import java.io.File
import java.io.FileNotFoundException
import java.nio.file.Files

/** Lets document browsers read the installed games and private instance data. */
class GameFoldersProvider : DocumentsProvider() {
    override fun onCreate() = true

    private val storage get() = GameStorage(requireNotNull(context))

    private fun roots(): Map<String, File> {
        val storage = storage
        val result = mutableMapOf("games" to File(storage.packageRoot, "games"))
        if (!storage.sharedEnabled) {
            val instances = storage.instances()
            val entries = instances.list()
            for (i in 0 until entries.length()) {
                val entry = entries.getJSONObject(i)
                result["user-" + entry.getString("id")] = File(entry.getString("directory"))
            }
        }
        return result
    }

    private fun resolve(id: String): File {
        val parts = id.split('/')
        val root = roots()[parts.first()] ?: throw FileNotFoundException(id)
        // Android may alias filesDir through /data/data or /data/user/0.
        // Trust that base only, and reject links at every level below it.
        val base = requireNotNull(context).filesDir
        val file = (root.relativeTo(base).toPath().map { it.toString() } + parts.drop(1)).fold(base) { parent, name ->
            if (name.isEmpty() || name == "." || name == "..") throw FileNotFoundException(id)
            File(parent, name).also { if (Files.isSymbolicLink(it.toPath())) throw FileNotFoundException(id) }
        }
        if (!file.exists()) throw FileNotFoundException(id)
        return file
    }

    override fun queryRoots(projection: Array<out String>?): MatrixCursor {
        val cursor = MatrixCursor(projection ?: arrayOf(Root.COLUMN_ROOT_ID, Root.COLUMN_DOCUMENT_ID,
            Root.COLUMN_TITLE, Root.COLUMN_FLAGS, Root.COLUMN_ICON, Root.COLUMN_MIME_TYPES))
        roots().filterValues { it.isDirectory }.forEach { (id, file) ->
            val row = cursor.newRow()
            row.add(Root.COLUMN_ROOT_ID, id)
            row.add(Root.COLUMN_DOCUMENT_ID, id)
            row.add(Root.COLUMN_TITLE, "tML Pocket Launcher · " + if (id == "games") file.name else id)
            row.add(Root.COLUMN_FLAGS, Root.FLAG_SUPPORTS_IS_CHILD)
            row.add(Root.COLUMN_ICON, R.mipmap.ic_launcher)
            row.add(Root.COLUMN_MIME_TYPES, "*/*")
        }
        return cursor
    }

    private fun cursor(projection: Array<out String>?) = MatrixCursor(projection ?: arrayOf(
        Document.COLUMN_DOCUMENT_ID, Document.COLUMN_DISPLAY_NAME, Document.COLUMN_MIME_TYPE,
        Document.COLUMN_SIZE, Document.COLUMN_LAST_MODIFIED, Document.COLUMN_FLAGS))

    private fun MatrixCursor.addFile(id: String, file: File) {
        val row = newRow()
        row.add(Document.COLUMN_DOCUMENT_ID, id)
        row.add(Document.COLUMN_DISPLAY_NAME, file.name)
        row.add(Document.COLUMN_MIME_TYPE, if (file.isDirectory) Document.MIME_TYPE_DIR
            else MimeTypeMap.getSingleton().getMimeTypeFromExtension(file.extension.lowercase()) ?: "application/octet-stream")
        row.add(Document.COLUMN_SIZE, if (file.isFile) file.length() else null)
        row.add(Document.COLUMN_LAST_MODIFIED, file.lastModified())
        row.add(Document.COLUMN_FLAGS, 0)
    }

    override fun queryDocument(documentId: String, projection: Array<out String>?) =
        cursor(projection).apply { addFile(documentId, resolve(documentId)) }

    override fun queryChildDocuments(parentDocumentId: String, projection: Array<out String>?, sortOrder: String?) =
        cursor(projection).apply {
            val children = resolve(parentDocumentId).listFiles() ?: throw FileNotFoundException(parentDocumentId)
            for (child in children) if (!Files.isSymbolicLink(child.toPath()))
                addFile("$parentDocumentId/${child.name}", child)
        }

    override fun isChildDocument(parentDocumentId: String, documentId: String) =
        documentId.startsWith("$parentDocumentId/") && runCatching { resolve(documentId) }.isSuccess

    override fun findDocumentPath(parentDocumentId: String?, childDocumentId: String): DocumentsContract.Path {
        resolve(childDocumentId)
        val rootId = childDocumentId.substringBefore('/')
        val parent = parentDocumentId ?: rootId
        if (parent != childDocumentId && !isChildDocument(parent, childDocumentId)) throw FileNotFoundException(childDocumentId)
        val path = generateSequence(childDocumentId) { if (it == parent) null else it.substringBeforeLast('/') }.toList().asReversed()
        return DocumentsContract.Path(if (parentDocumentId == null) rootId else null, path)
    }

    override fun openDocument(documentId: String, mode: String, signal: CancellationSignal?): ParcelFileDescriptor {
        if (mode != "r") throw FileNotFoundException("Read-only")
        return ParcelFileDescriptor.open(resolve(documentId), ParcelFileDescriptor.MODE_READ_ONLY)
    }
}
