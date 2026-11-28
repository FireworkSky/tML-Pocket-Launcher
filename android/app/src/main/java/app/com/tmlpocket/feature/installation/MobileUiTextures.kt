package app.com.tmlpocket.feature.installation

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.graphics.Rect
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.min
import kotlin.math.roundToInt
import kotlin.math.sin

internal object MobileUiTextures {
    private val ink = Color.rgb(18, 29, 48)
    private val slate = Color.rgb(45, 67, 94)
    private val edge = Color.rgb(121, 158, 184)
    private val mint = Color.rgb(111, 215, 189)
    private val gold = Color.rgb(255, 200, 101)
    private val white = Color.rgb(231, 241, 244)

    fun write(output: File, textures: Map<String, Pair<Int, Int>>) {
        if (textures.isEmpty()) return
        val sprites = mutableMapOf<String, Bitmap>()
        try {
            fun sprite(name: String): Bitmap = sprites.getOrPut(name) {
                readMobileSprite(File(output, "mobile-controls/$name.rawimg"))
            }
            for ((path, size) in textures) {
                val (width, height) = size
                require(width in 1..1024 && height in 1..1024)
                val bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
                try {
                    draw(UiCanvas(bitmap, ::sprite), path.removePrefix("Images/UI/").removeSuffix(".xnb"))
                    writeXnb(File(output, "Content/$path"), bitmap)
                } finally {
                    bitmap.recycle()
                }
            }
        } finally {
            sprites.values.forEach(Bitmap::recycle)
        }
    }

    private fun readMobileSprite(file: File): Bitmap {
        val bytes = ByteBuffer.wrap(file.readBytes()).order(ByteOrder.LITTLE_ENDIAN)
        require(bytes.int == 1)
        val width = bytes.int
        val height = bytes.int
        require(width in 1..1024 && height in 1..1024 && bytes.remaining() == width * height * 4)
        val colors = IntArray(width * height) {
            val rgba = bytes.int
            val alpha = rgba ushr 24
            fun straight(shift: Int) = if (alpha == 0) 0 else (((rgba ushr shift and 255) * 255 + alpha / 2) / alpha).coerceAtMost(255)
            Color.argb(alpha, straight(0), straight(8), straight(16))
        }
        return Bitmap.createBitmap(colors, width, height, Bitmap.Config.ARGB_8888)
    }

    private fun writeXnb(file: File, bitmap: Bitmap) {
        val reader = "Microsoft.Xna.Framework.Content.Texture2DReader".toByteArray(Charsets.US_ASCII)
        val width = bitmap.width
        val height = bitmap.height
        val pixels = IntArray(width * height)
        bitmap.getPixels(pixels, 0, width, 0, 0, width, height)
        val bytes = ByteBuffer.allocate(38 + reader.size + pixels.size * 4).order(ByteOrder.LITTLE_ENDIAN)
        bytes.put(byteArrayOf(88, 78, 66, 119, 5, 1)).putInt(bytes.capacity())
        bytes.put(1.toByte()).put(reader.size.toByte()).put(reader).putInt(0).put(0.toByte()).put(1.toByte())
        bytes.putInt(0).putInt(width).putInt(height).putInt(1).putInt(pixels.size * 4)
        for (pixel in pixels) {
            val alpha = Color.alpha(pixel)
            bytes.put((Color.red(pixel) * alpha / 255).toByte())
            bytes.put((Color.green(pixel) * alpha / 255).toByte())
            bytes.put((Color.blue(pixel) * alpha / 255).toByte())
            bytes.put(alpha.toByte())
        }
        check(!bytes.hasRemaining())
        file.parentFile?.mkdirs()
        file.writeBytes(bytes.array())
    }

    private fun draw(ui: UiCanvas, name: String) = with(ui) {
        when (name) {
            "Achievement_InnerPanelBottom", "Achievement_InnerPanelBottom_Large", "Achievement_InnerPanelTop" ->
                panel(slate, edge)
            "Bestiary" -> {
                sprite("MobileMenu/PauseBestiary", 0f, 0f, 32f, height)
                sprite("MobileMenu/PauseBestiary", 32f, 0f, 32f, height)
                frame(33f, 2f, 30f, height - 4, gold)
            }
            "Bestiary/Biome_Hallow" -> {
                biome(0f, false)
                biome(width / 2, true)
            }
            "Bestiary/Button_Back", "Bestiary/Button_Forward" ->
                glyph(if (name.endsWith("Forward")) "right" else "left", 0f, 0f, width, height, white)
            "Bestiary/Button_Border", "Bestiary/Button_Search_Border", "Bestiary/Button_Wide_Border",
            "Bestiary/Slot_Selection", "Creative/Journey_Toggle_MouseOver" -> frame(1f, 1f, width - 2, height - 2, gold)
            "Bestiary/Button_Filtering", "Bestiary/Button_Sorting", "Bestiary/Button_Search" -> {
                panel(slate, edge)
                val icon = when {
                    name.endsWith("Sorting") -> null
                    name.endsWith("Filtering") -> "MobileMenu/WorldGeneral"
                    else -> "MobileInventory/Search"
                }
                if (icon == null) glyph("down", 2f, 2f, min(width, height) - 4, height - 4, white)
                else sprite(icon, 2f, 2f, min(width, height) - 4, height - 4)
            }
            "Bestiary/Slot_Back" -> panel(ink, slate)
            "Bestiary/Slot_Front" -> frame(1f, 1f, width - 2, height - 2, edge)
            "Bestiary/Slot_Overlay" -> panel(Color.argb(168, 27, 42, 67))
            "Bestiary/Stat_Panel" -> panel(white)
            "Creative/Infinite_Icons" -> {
                val icons = listOf("Category10", "Category9", "Loadout", "Category7", "Category6", "Bag",
                    "Tool", "House", "Category0", "Category8", "Craft")
                icons.forEachIndexed { index, icon ->
                    sprite("MobileInventory/$icon", index * 30f, 0f, 28f, height)
                }
            }
            "Creative/Infinite_Tabs_A", "Creative/Infinite_Tabs_B" -> {
                val columns = if (name.endsWith("_A")) 4 else 2
                val rows = if (name.endsWith("_A")) 2 else 4
                cells(columns, rows) { index, x, y, w, h ->
                    if (index in 2..3) frame(x + 2, y + 2, w - 4, h - 4, gold)
                    else sprite(if (index % 2 == 0) "MobileMenu/SelectionNormal" else "MobileInventory/TabSelected", x, y, w, h)
                }
            }
            "Creative/Journey_Toggle" -> sprite("MobileMenu/PauseJourney")
            "DefaultResourcePackIcon", "ModPackLocation" -> packageIcon()
            "DisplaySlots_0", "DisplaySlots_1" -> sprite("MobileInventory/Loadout")
            "DisplaySlots_2", "DisplaySlots_3", "DisplaySlots_6" -> {
                if (name.endsWith("_6")) frame(1f, 1f, width - 2, height - 2, gold)
                sprite("MobileInventory/Equipment")
            }
            "DisplaySlots_7" -> sprite("MobileInventory/House")
            "DisplaySlots_8", "DisplaySlots_9", "DisplaySlots_10" -> Unit // Photo mode is disabled for PE.
            "HotbarRadial_0" -> {
                panel(slate, edge)
                sprite("MobileControls/DirectionalArrowsOriginal", width / 4, height / 4, width / 2, height / 2)
            }
            "HotbarRadial_1" -> sprite("MobileInventory/SlotSelected")
            "HotbarRadial_2" -> sprite("MobileControls/PhoneRadialRing")
            "Scrollbar" -> panel(ink, edge)
            "ScrollbarInner" -> sprite("MobileInventory/SliderKnob")
            "Settings_Inputs", "Settings_Inputs_2" -> cells(2, 2) { index, x, y, w, h ->
                val color = if (index < 2) slate else ink
                round(x + 2, y + 2, w - 4, h - 4, color)
                if (name == "Settings_Inputs" && index % 2 == 0) {
                    glyph("keyboard", x + w * .25f, y + h * .2f, w * .5f, h * .6f, if (index < 2) white else edge)
                } else {
                    val icon = when {
                        name == "Settings_Inputs" -> "MobileMenu/Tab6"
                        index % 2 == 0 -> "MobileInventory/Tool"
                        else -> "MobileMenu/WorldGeneral"
                    }
                    sprite(icon, x + 3, y + 3, w - 6, h - 6, alpha = if (index < 2) 255 else 130)
                }
            }
            "Settings_Inputs_Border", "Settings_Inputs_2_Border" -> frame(1f, 1f, width - 2, height - 2, white)
            "Settings_Panel", "Settings_Panel_2" -> panel(white, edge)
            "Settings_Toggle" -> {
                val half = (width - 2f) / 2f
                sprite("MobileMenu/ToggleOff", 0f, 0f, half, height)
                sprite("MobileMenu/ToggleOn", half + 2, 0f, half, height)
            }
            "UI_BossBar" -> bossBar()
            "VK_Backspace" -> glyph("backspace", 0f, 0f, width, height, white)
            "VK_Shift" -> glyph("upload", 0f, 0f, width, height, white)
            "Workshop/DefaultPreviewImage" -> sprite("MobileMenu/WorldPreviewSky")
            "Workshop/HubPublishResourcepacks", "Workshop/HubResourcepacks", "Workshop/HubPublishWorlds", "Workshop/HubWorlds" -> {
                panel(ink, edge)
                if (name.endsWith("Worlds")) sprite("MobileMenu/WorldPreviewMedium", 5f, 4f, width - 10, height - 8)
                else packageIcon()
                if (name.contains("Publish")) glyph("upload", width * .61f, height * .62f, width * .34f, height * .34f, mint)
            }
            "Workshop/InProgress" -> cells(1, 6) { index, x, y, w, h ->
                spinner(index, x + w / 2, y + h / 2, min(w, h) * .36f)
            }
            "Workshop/IssueButton" -> glyph("warning", 0f, 0f, width, height, gold)
            "Workshop/IssueButton_Outline" -> frame(2f, 2f, width - 4, height - 4, gold)
            "Workshop/ListBackground" -> panel(slate, edge)
            "Workshop/PublicityFriendsOnly" -> glyph("people", 0f, 0f, width, height, white)
            "Workshop/PublicityPrivate" -> glyph("lock", 0f, 0f, width, height, white)
            "Workshop/PublicityPublic" -> sprite("MobileMenu/WorldSizeLarge")
            "Workshop/Publish" -> glyph("upload", 0f, 0f, width, height, mint)
            "Workshop/Tags" -> glyph("tag", 0f, 0f, width, height, white)
            else -> error("Missing PE UI drawing rule: $name")
        }
    }

    private class UiCanvas(bitmap: Bitmap, private val source: (String) -> Bitmap) {
        val width = bitmap.width.toFloat()
        val height = bitmap.height.toFloat()
        private val canvas = Canvas(bitmap)
        private val paint = Paint()

        fun round(x: Float, y: Float, w: Float, h: Float, color: Int, radius: Float = min(w, h) * .15f) {
            paint.color = color
            paint.style = Paint.Style.FILL
            canvas.drawRoundRect(x, y, x + w, y + h, radius, radius, paint)
        }

        fun frame(x: Float, y: Float, w: Float, h: Float, color: Int) {
            paint.color = color
            paint.style = Paint.Style.STROKE
            paint.strokeWidth = 2f
            val corner = min(5f, min(w, h) / 5f)
            canvas.drawRoundRect(x, y, x + w, y + h, corner, corner, paint)
            paint.style = Paint.Style.FILL
        }

        fun panel(fill: Int, outline: Int? = null) {
            round(1f, 1f, width - 2, height - 2, fill, min(6f, width / 4))
            if (outline != null) frame(1f, 1f, width - 2, height - 2, outline)
        }

        fun sprite(name: String, x: Float = 0f, y: Float = 0f, w: Float = width, h: Float = height,
                   flip: Boolean = false, alpha: Int = 255) {
            val image = source(name)
            val scale = min(w / image.width, h / image.height)
            val dw = (image.width * scale).roundToInt().coerceAtLeast(1)
            val dh = (image.height * scale).roundToInt().coerceAtLeast(1)
            val left = (x + (w - dw) / 2).roundToInt()
            val top = (y + (h - dh) / 2).roundToInt()
            paint.color = Color.WHITE
            paint.alpha = alpha
            canvas.save()
            if (flip) canvas.scale(-1f, 1f, x + w / 2, y + h / 2)
            canvas.drawBitmap(image, null, Rect(left, top, left + dw, top + dh), paint)
            canvas.restore()
            paint.alpha = 255
        }

        fun cells(columns: Int, rows: Int, draw: (Int, Float, Float, Float, Float) -> Unit) {
            val w = width / columns
            val h = height / rows
            for (index in 0 until columns * rows) draw(index, index % columns * w, index / columns * h, w, h)
        }

        private fun shape(color: Int, vararg points: Float) {
            val path = Path()
            path.moveTo(points[0], points[1])
            for (i in 2 until points.size step 2) path.lineTo(points[i], points[i + 1])
            path.close()
            paint.color = color
            canvas.drawPath(path, paint)
        }

        private fun dot(x: Float, y: Float, radius: Float, color: Int) {
            paint.color = color
            canvas.drawCircle(x, y, radius, paint)
        }

        private fun normalized(x: Float, y: Float, w: Float, h: Float, draw: () -> Unit) {
            canvas.save()
            canvas.translate(x, y)
            canvas.scale(w / 24, h / 24)
            draw()
            canvas.restore()
        }

        fun packageIcon() = normalized(0f, 0f, width, height) {
            round(4f, 9f, 16f, 11f, slate, 2f)
            shape(mint, 3f, 9f, 6f, 5f, 18f, 5f, 21f, 9f, 19f, 12f, 5f, 12f)
            round(10f, 6f, 4f, 10f, gold, 1f)
        }

        fun glyph(name: String, x: Float, y: Float, w: Float, h: Float, color: Int) = normalized(x, y, w, h) {
            when (name) {
                "left" -> shape(color, 16f, 3f, 6f, 12f, 16f, 21f)
                "right" -> shape(color, 8f, 3f, 18f, 12f, 8f, 21f)
                "down" -> shape(color, 3f, 8f, 21f, 8f, 12f, 18f)
                "warning" -> {
                    shape(color, 12f, 2f, 23f, 22f, 1f, 22f)
                    round(11f, 8f, 2f, 7f, ink, 1f)
                    dot(12f, 18f, 1.3f, ink)
                }
                "keyboard" -> {
                    round(2f, 5f, 20f, 14f, color, 2f)
                    for (row in 0..1) for (col in 0..3)
                        round(5f + col * 4, 8f + row * 3, 2f, 2f, ink, 0f)
                    round(7f, 14f, 10f, 2f, ink, 0f)
                }
                "backspace" -> {
                    shape(color, 2f, 12f, 8f, 5f, 22f, 5f, 22f, 19f, 8f, 19f)
                    shape(ink, 10f, 8f, 12f, 8f, 18f, 16f, 16f, 16f)
                    shape(ink, 16f, 8f, 18f, 8f, 12f, 16f, 10f, 16f)
                }
                "upload" -> shape(color, 3f, 11f, 12f, 3f, 21f, 11f, 15f, 11f, 15f, 21f, 9f, 21f, 9f, 11f)
                "people" -> {
                    for (px in listOf(7f, 17f)) {
                        dot(px, 7f, 3f, color)
                        round(px - 4, 11f, 8f, 8f, color, 3f)
                    }
                }
                "lock" -> {
                    frame(7f, 3f, 10f, 11f, color)
                    round(5f, 10f, 14f, 11f, color, 2f)
                    dot(12f, 15f, 2f, ink)
                }
                "tag" -> {
                    shape(color, 3f, 13f, 13f, 3f, 21f, 3f, 21f, 11f, 11f, 21f)
                    dot(17f, 7f, 1.7f, ink)
                }
            }
        }

        fun biome(x: Float, unlocked: Boolean) = normalized(x, 0f, width / 2, height) {
            val light = if (unlocked) mint else edge
            round(0f, 0f, 24f, 24f, if (unlocked) slate else ink, 0f)
            dot(16f, 6f, 3f, if (unlocked) gold else slate)
            shape(light, 1f, 20f, 6f, 9f, 9f, 17f, 14f, 11f, 19f, 19f, 23f, 14f, 23f, 23f, 1f, 23f)
        }

        fun spinner(index: Int, x: Float, y: Float, radius: Float) {
            for (point in 0..5) {
                val angle = (index + point) * PI / 3
                val color = Color.argb(50 + point * 39, 111, 215, 189)
                dot(x + (cos(angle) * radius).toFloat(), y + (sin(angle) * radius).toFloat(), 2.5f, color)
            }
        }

        fun bossBar() {
            check(width == 516f && height == 348f)
            // The renderer crops six 58 px frames and stretches two-pixel fill strips.
            frame(28f, 21f, 464f, 28f, edge)
            round(1f, 17f, 28f, 34f, ink, 4f)
            round(32f, 3 * 58f + 24, 456f, 22f, ink, 0f)
            val colors = listOf(gold, Color.rgb(201, 92, 113), white, mint)
            for ((index, frame) in listOf(1, 2, 4, 5).withIndex()) {
                val y = frame * 58f + 24
                round(32f, y, 2f, 22f, colors[index], 0f)
                round(32f, y, 2f, 3f, white, 0f)
            }
        }
    }
}
