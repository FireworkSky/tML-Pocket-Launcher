package app.com.tmlpocket.feature.installation

import android.content.Context
import android.graphics.Bitmap
import android.util.Base64
import java.io.DataInputStream
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder
import org.json.JSONObject

internal object MobileControlArtPreview {
    private val inventoryTextures = listOf(
        "Slot", "SlotSelected", "SlotOccupied", "SlotGreen", "SlotTeal", "SlotPurple",
        "EmptyCoin", "EmptyAmmo", "EmptyHead", "EmptyBody", "EmptyLegs", "EmptyAccessory", "EmptyDye",
        "EmptyGrapple", "EmptyMount", "EmptyPet", "EmptyLightPet", "Settings", "Craft",
        "Category0", "Category2", "Category4", "Category5", "Category8", "Category9",
        "QuickStack", "Sort", "Restock", "DepositAll", "LootAll", "CraftLock", "Trash",
    )

    fun read(context: Context, root: File?, content: File?): JSONObject {
        val result = JSONObject()
        val manifest = context.assets.open("mobile-controls/control-textures.json").bufferedReader().use {
            JSONObject(it.readText())
        }
        val aliases = context.assets.open("mobile-controls/asset-aliases.json").bufferedReader().use {
            JSONObject(it.readText())
        }
        val paths = mutableMapOf<String, String>()
        val textures = manifest.getJSONObject("textures")
        for (id in textures.keys()) {
            paths[id] = textures.getJSONObject(id).getString("file").replaceAfterLast('.', "rawimg")
        }
        val mappings = manifest.getJSONObject("mappings")
        for (name in mappings.keys()) {
            paths[name] = paths.getValue(mappings.getString(name))
        }
        for (name in inventoryTextures) {
            val path = "MobileInventory/$name"
            paths[path] = "$path.rawimg"
        }
        val drawings by lazy { TouchDrawings.load(context) }
        val images = paths.values.toSet().associateWith { path ->
            val name = path.removeSuffix(".rawimg")
            val original = root?.resolve(aliases.optString(name, name) + ".rawimg")
            original?.takeIf { it.isFile }?.let { runCatching { encodePng(it) }.getOrNull() }
                ?: name.takeIf { drawings.recipes.has(it) }?.let { encodePng(drawings.render(it)) }
        }
        for ((name, path) in paths) images[path]?.let { result.put(name, it) }
        for (id in listOf(28, 110, 2349)) {
            if (content != null) runCatching {
                encodePng(ConvertedTexturePreview.rawImage(File(content, "Images/Item_$id.xnb")))
            }.getOrNull()?.let { result.put("Items/Item_$id", it) }
            val path = "Items/Item_$id"
            if (!result.has(path)) result.put(path, encodePng(drawings.render(path)))
        }
        return result
    }

    private fun encodePng(file: File): String {
        require(file.isFile && file.length() in 13..(16L * 1024 * 1024))
        return encodePng(file.readBytes())
    }

    private fun encodePng(bytes: ByteArray): String {
        DataInputStream(bytes.inputStream()).use { input ->
            val header = ByteArray(12)
            input.readFully(header)
            val fields = ByteBuffer.wrap(header).order(ByteOrder.LITTLE_ENDIAN)
            require(fields.int == 1)
            val width = fields.int
            val height = fields.int
            require(width in 1..4096 && height in 1..4096 && width.toLong() * height <= 4_000_000)
            val rgba = ByteArray(width * height * 4)
            input.readFully(rgba)
            require(input.read() == -1)
            val pixels = IntArray(width * height) { index ->
                val offset = index * 4
                val alpha = rgba[offset + 3].toInt() and 0xff
                val red = unpremultiply(rgba[offset].toInt() and 0xff, alpha)
                val green = unpremultiply(rgba[offset + 1].toInt() and 0xff, alpha)
                val blue = unpremultiply(rgba[offset + 2].toInt() and 0xff, alpha)
                (alpha shl 24) or (red shl 16) or (green shl 8) or blue
            }
            return encodePng(Bitmap.createBitmap(pixels, width, height, Bitmap.Config.ARGB_8888))
        }
    }

    private fun encodePng(bitmap: Bitmap): String {
        return try {
            val png = java.io.ByteArrayOutputStream()
            check(bitmap.compress(Bitmap.CompressFormat.PNG, 100, png))
            "data:image/png;base64," + Base64.encodeToString(png.toByteArray(), Base64.NO_WRAP)
        } finally {
            bitmap.recycle()
        }
    }

    private fun unpremultiply(channel: Int, alpha: Int): Int =
        if (alpha == 0) 0 else (channel * 255 / alpha).coerceAtMost(255)
}
