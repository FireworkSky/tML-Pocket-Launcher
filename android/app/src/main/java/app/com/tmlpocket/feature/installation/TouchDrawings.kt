package app.com.tmlpocket.feature.installation

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.graphics.PorterDuff
import android.graphics.PorterDuffXfermode
import android.graphics.RectF
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.security.MessageDigest
import org.json.JSONArray
import org.json.JSONObject

/** Rasterizes the launcher's own geometric drawings, on device. */
internal class TouchDrawings private constructor(val recipes: JSONObject, private val revision: String) {
    companion object {
        @Volatile private var instance: TouchDrawings? = null

        fun load(context: Context): TouchDrawings = instance ?: synchronized(this) {
            instance ?: context.assets.open("launcher/touch-drawings.json").use { stream ->
                val bytes = stream.readBytes()
                val digest = MessageDigest.getInstance("SHA-256").digest(bytes)
                TouchDrawings(JSONObject(bytes.toString(Charsets.UTF_8)), digest.take(12).joinToString("") { "%02x".format(it) })
                    .also { instance = it }
            }
        }
    }

    fun render(name: String): Bitmap {
        val recipe = recipes.getJSONArray(name)
        val size = recipe.getJSONArray(0)
        val viewport = recipe.getJSONArray(1)
        val bitmap = Bitmap.createBitmap(size.getInt(0), size.getInt(1), Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        canvas.scale(bitmap.width / viewport.number(0), bitmap.height / viewport.number(1))
        draw(canvas, recipe.getJSONArray(2))
        return bitmap
    }

    fun writeRaw(name: String, destination: File) {
        val parent = checkNotNull(destination.parentFile)
        check(parent.isDirectory || parent.mkdirs())
        val temp = File.createTempFile(".draw-", ".tmp", parent)
        try {
            val bitmap = render(name)
            try {
                val data = ByteBuffer.allocate(12 + bitmap.byteCount).order(ByteOrder.LITTLE_ENDIAN)
                data.putInt(1).putInt(bitmap.width).putInt(bitmap.height)
                bitmap.copyPixelsToBuffer(data) // Android stores premultiplied RGBA in native memory.
                temp.writeBytes(data.array())
            } finally {
                bitmap.recycle()
            }
            Files.move(temp.toPath(), destination.toPath(), StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING)
        } finally {
            temp.delete()
        }
    }

    // The managed texture loader consumes rawimg files. A cache entry is published only
    // after its complete contents have been written. Original files stay in their own root.
    fun prepareGame(context: Context, originalRoot: String): File {
        val root = File(context.cacheDir, "drawn-touch-v1/$revision")
        check(root.isDirectory || root.mkdirs())
        val aliases = context.assets.open("mobile-controls/asset-aliases.json").bufferedReader().use { JSONObject(it.readText()) }
        for (name in recipes.keys()) {
            if (originalRoot.isNotEmpty() && File(originalRoot, aliases.optString(name, name) + ".rawimg").isFile) continue
            val destination = File(root, "$name.rawimg")
            val size = recipes.getJSONArray(name).getJSONArray(0)
            if (destination.length() == 12L + size.getInt(0) * size.getInt(1) * 4) continue
            writeRaw(name, destination)
        }
        return root
    }

    private fun JSONArray.number(index: Int) = getDouble(index).toFloat()

    private fun draw(canvas: Canvas, commands: JSONArray, silhouette: String? = null) {
        val paint = Paint().apply { isAntiAlias = false; strokeJoin = Paint.Join.ROUND }
        for (i in 0 until commands.length()) {
            val op = commands.getJSONArray(i)
            val type = op.getString(0)
            if (type == "layer") {
                val box = op.getJSONArray(1)
                canvas.save()
                canvas.translate(box.number(0), box.number(1))
                canvas.scale(box.number(2) / 32f, box.number(3) / 32f)
                canvas.rotate(op.number(2), 16f, 16f)
                if (op.getBoolean(3)) canvas.scale(-1f, 1f, 16f, 16f)
                draw(canvas, op.getJSONArray(4), silhouette ?: op.optString(5).takeIf { it.isNotEmpty() })
                canvas.restore()
                continue
            }
            if (type == "tint") {
                val rgb = Color.parseColor(op.getString(1)) and 0xffffff
                canvas.drawColor(rgb or (op.getInt(2) shl 24), PorterDuff.Mode.SRC_ATOP)
                continue
            }
            val coords = op.getJSONArray(1)
            val path = Path()
            when (type) {
                "line", "polygon" -> {
                    for (j in 0 until coords.length()) {
                        val point = coords.getJSONArray(j)
                        if (j == 0) path.moveTo(point.number(0), point.number(1))
                        else path.lineTo(point.number(0), point.number(1))
                    }
                    if (type == "polygon") path.close()
                }
                "box", "oval", "arc" -> {
                    val bounds = RectF(coords.number(0), coords.number(1), coords.number(2) + 1, coords.number(3) + 1)
                    when (type) {
                        "box" -> path.addRoundRect(bounds, op.number(4), op.number(4), Path.Direction.CW)
                        "oval" -> path.addOval(bounds, Path.Direction.CW)
                        else -> path.addArc(bounds, op.number(3), op.number(4))
                    }
                }
                else -> error("Unknown touch drawing command $type")
            }
            fun paintPath(index: Int, style: Paint.Style, width: Float = 1f) {
                if (op.isNull(index)) return
                val color = Color.parseColor(op.getString(index))
                paint.color = if (silhouette != null && Color.alpha(color) != 0) Color.parseColor(silhouette) else color
                paint.xfermode = if (Color.alpha(color) == 0) PorterDuffXfermode(PorterDuff.Mode.CLEAR) else null
                paint.style = style
                paint.strokeWidth = width
                canvas.drawPath(path, paint)
            }
            if (type == "line" || type == "arc") {
                paintPath(2, Paint.Style.STROKE, op.number(op.length() - 1))
            } else {
                paintPath(2, Paint.Style.FILL)
                paintPath(3, Paint.Style.STROKE, if (type == "polygon") 1f else op.number(op.length() - 1))
            }
        }
    }
}
