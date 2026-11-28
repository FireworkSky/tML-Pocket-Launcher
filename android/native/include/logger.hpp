#pragma once

#include <android/log.h>

namespace mobile_launcher {

void capture_standard_streams();
void append_game_log(const char* source, const char* message);

inline void log_info(const char* message) {
    __android_log_print(ANDROID_LOG_INFO, "tMLPocketCoreClr", "%s", message);
}

inline void log_warn(const char* message) {
    __android_log_print(ANDROID_LOG_WARN, "tMLPocketCoreClr", "%s", message);
}

inline void log_error(const char* message) {
    append_game_log("error", message);
    __android_log_print(ANDROID_LOG_ERROR, "tMLPocketCoreClr", "%s", message);
}

}
