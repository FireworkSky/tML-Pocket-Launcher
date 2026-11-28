#include "logger.hpp"

#include <cerrno>
#include <cstdio>
#include <ctime>
#include <jni.h>
#include <SDL3/SDL_log.h>
#include <fcntl.h>
#include <mutex>
#include <new>
#include <pthread.h>
#include <string>
#include <unistd.h>

namespace {
std::mutex game_log_mutex;
std::string game_log_path;
FILE* game_log = nullptr;
SDL_LogOutputFunction previous_sdl_log = nullptr;
void* previous_sdl_data = nullptr;

void SDLCALL log_sdl(void*, int category, SDL_LogPriority priority, const char* message) {
    mobile_launcher::append_game_log(priority >= SDL_LOG_PRIORITY_ERROR ? "SDL error" :
        priority >= SDL_LOG_PRIORITY_WARN ? "SDL warning" : "SDL", message);
    if (previous_sdl_log) previous_sdl_log(previous_sdl_data, category, priority, message);
}

struct StreamReader {
    int fd;
    int priority;
};

void* read_stream(void* argument) {
    const auto reader = *static_cast<StreamReader*>(argument);
    delete static_cast<StreamReader*>(argument);
    char buffer[1024];
    std::string line;
    auto emit = [&]() {
        if (!line.empty()) {
            __android_log_write(reader.priority, "tMLPocketNative", line.c_str());
            mobile_launcher::append_game_log(reader.priority >= ANDROID_LOG_ERROR ? "stderr" : "stdout", line.c_str());
            line.clear();
        }
    };
    while (true) {
        const auto count = read(reader.fd, buffer, sizeof(buffer));
        if (count < 0 && errno == EINTR) continue;
        if (count <= 0) break;
        for (ssize_t i = 0; i < count; ++i) {
            const auto value = buffer[i];
            if (value == '\n') emit();
            else {
                // Split before the next UTF-8 character, not within one.
                if (line.size() >= 3000 &&
                    (static_cast<unsigned char>(value) & 0xc0) != 0x80) emit();
                line += value == '\0' ? '?' : value;
                // Also bound malformed input containing only continuation bytes.
                if (line.size() >= 3500) emit();
            }
        }
    }
    emit();
    close(reader.fd);
    return nullptr;
}

void redirect_stream(FILE* stream, int target, int priority) {
    int descriptors[2];
    if (pipe2(descriptors, O_CLOEXEC) != 0) {
        mobile_launcher::log_warn("Could not create native log pipe");
        return;
    }
    auto* reader = new (std::nothrow) StreamReader{descriptors[0], priority};
    pthread_t thread;
    if (reader == nullptr || pthread_create(&thread, nullptr, read_stream, reader) != 0) {
        delete reader;
        close(descriptors[0]);
        close(descriptors[1]);
        mobile_launcher::log_warn("Could not start native log reader");
        return;
    }
    pthread_detach(thread);
    fflush(stream);
    if (dup2(descriptors[1], target) < 0) {
        mobile_launcher::log_warn("Could not redirect native log stream");
    } else {
        setvbuf(stream, nullptr, _IONBF, 0);
    }
    close(descriptors[1]);
}
}

void mobile_launcher::append_game_log(const char* source, const char* message) {
    std::lock_guard lock(game_log_mutex);
    if (!game_log || !message) return;
    if (ftell(game_log) >= 512 * 1024) {
        fclose(game_log);
        rename(game_log_path.c_str(), (game_log_path + ".1").c_str());
        game_log = fopen(game_log_path.c_str(), "we");
        if (!game_log) return;
    }
    timespec now{};
    clock_gettime(CLOCK_REALTIME, &now);
    fprintf(game_log, "%lld.%03ld [%s] %.8192s\n", static_cast<long long>(now.tv_sec),
        now.tv_nsec / 1000000, source, message);
    fflush(game_log);
}

extern "C" JNIEXPORT jboolean JNICALL
Java_app_com_tmlpocket_core_platform_NativeCoreClr_nativeStartGameLog(JNIEnv* env, jclass, jstring path) {
    const char* chars = env->GetStringUTFChars(path, nullptr);
    if (!chars) return JNI_FALSE;
    {
        std::lock_guard lock(game_log_mutex);
        if (game_log) fclose(game_log);
        game_log_path = chars;
        game_log = fopen(chars, "ae");
        env->ReleaseStringUTFChars(path, chars);
        if (!game_log) return JNI_FALSE;
    }
    static std::once_flag installed;
    std::call_once(installed, [] {
        SDL_GetLogOutputFunction(&previous_sdl_log, &previous_sdl_data);
        SDL_SetLogOutputFunction(log_sdl, nullptr);
    });
    mobile_launcher::append_game_log("startup", "Game process logging started");
    return JNI_TRUE;
}

extern "C" __attribute__((visibility("default")))
void tml_pocket_managed_log(int priority, const char* message) {
    mobile_launcher::append_game_log(priority >= ANDROID_LOG_ERROR ? ".NET error" : ".NET", message);
}

void mobile_launcher::capture_standard_streams() {
    static std::once_flag initialized;
    std::call_once(initialized, [] {
        redirect_stream(stdout, STDOUT_FILENO, ANDROID_LOG_INFO);
        redirect_stream(stderr, STDERR_FILENO, ANDROID_LOG_ERROR);
        log_info("Native stdout/stderr capture initialized");
    });
}

extern "C" void mobile_launcher_log_info(const char* message) {
    mobile_launcher::log_info(message);
}
