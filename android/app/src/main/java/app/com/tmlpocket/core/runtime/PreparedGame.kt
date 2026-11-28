package app.com.tmlpocket.core.runtime

data class PreparedGame(
    val assemblyPath: String,
    val runtimeRoot: String,
    val workingDirectory: String,
    val arguments: Array<String>,
    val tmlAssembly: String,
    val contentDirectory: String,
    val terrariaDirectory: String,
    val controlArtDirectory: String,
    val userDataDirectory: String
)
