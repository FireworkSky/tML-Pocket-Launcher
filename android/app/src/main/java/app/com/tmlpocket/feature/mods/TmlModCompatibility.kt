package app.com.tmlpocket.feature.mods

internal object TmlModCompatibility {
    fun accepts(builtWith: String, target: String): Boolean {
        val mod = parse(builtWith) ?: return false
        val runtime = parse(target) ?: return false
        // Match tML's SocialBrowserModule release streams and ModOrganizer's
        // monthly-version selection. Transitional 1.4.4 builds are unsupported.
        val stream = stream(mod)
        return stream != 0 && stream == stream(runtime) &&
            (mod[0] < runtime[0] || mod[0] == runtime[0] && mod[1] <= runtime[1])
    }

    private fun parse(value: String): List<Int>? {
        val parts = value.removePrefix("v").split('.')
        if (parts.size !in 2..4) return null
        val numbers = parts.map { it.toIntOrNull() ?: return null }
        if (numbers.any { it < 0 }) return null
        return List(4) { numbers.getOrElse(it) { 0 } }
    }

    private fun stream(version: List<Int>): Int = when {
        before(version, listOf(0, 12, 0, 0)) -> 13
        before(version, listOf(2022, 10, 0, 0)) -> 143
        before(version, listOf(2023, 3, 85, 0)) -> 0
        else -> 144
    }

    private fun before(left: List<Int>, right: List<Int>): Boolean {
        for (index in left.indices) {
            if (left[index] != right[index]) return left[index] < right[index]
        }
        return false
    }
}
