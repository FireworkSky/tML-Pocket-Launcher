#include "android_bridge.hpp"

#include <atomic>
#include <deque>
#include <mutex>
#include <string>

#include <jni.h>

#include "jni_entry.hpp"

namespace {

constexpr std::size_t kMaxEvents = 1024;
static_assert(TML_POCKET_ANDROID_EVENT_LAUNCHER_ACTION == 7);
static_assert(TML_POCKET_ANDROID_EVENT_IME_COMMIT == 8);
static_assert(TML_POCKET_ANDROID_EVENT_IME_COMPOSITION == 9);
static_assert(TML_POCKET_ANDROID_EVENT_IME_VISIBILITY == 10);

struct QueuedAndroidEvent {
    TmlPocketAndroidEvent event;
    std::string text;
};

std::mutex g_bridge_mutex;
jobject g_activity = nullptr;
jclass g_bridge_class = nullptr;
std::deque<QueuedAndroidEvent> g_queued_events;
std::atomic_bool g_back_requested{false};
std::atomic_bool g_keyboard_requested{false};
thread_local std::string g_last_event_text;
thread_local std::string g_last_clipboard_text;

void clear_exception(JNIEnv* env) {
    if (env->ExceptionCheck()) {
        env->ExceptionDescribe();
        env->ExceptionClear();
    }
}

int call_int(JNIEnv* env, const char* name, const char* signature, jvalue* arguments) {
    std::lock_guard lock(g_bridge_mutex);
    if (g_bridge_class == nullptr) return -1;

    const jmethodID method = env->GetStaticMethodID(g_bridge_class, name, signature);
    if (method == nullptr) {
        clear_exception(env);
        return -1;
    }

    const jint result = env->CallStaticIntMethodA(g_bridge_class, method, arguments);
    if (env->ExceptionCheck()) {
        clear_exception(env);
        return -1;
    }
    return result;
}

std::int64_t call_long(JNIEnv* env, const char* name, const char* signature, jvalue* arguments) {
    std::lock_guard lock(g_bridge_mutex);
    if (g_bridge_class == nullptr) return 0;

    const jmethodID method = env->GetStaticMethodID(g_bridge_class, name, signature);
    if (method == nullptr) {
        clear_exception(env);
        return 0;
    }

    const jlong result = env->CallStaticLongMethodA(g_bridge_class, method, arguments);
    if (env->ExceptionCheck()) {
        clear_exception(env);
        return 0;
    }
    return static_cast<std::int64_t>(result);
}

bool call_bool(JNIEnv* env, const char* name, const char* signature, jvalue* arguments) {
    std::lock_guard lock(g_bridge_mutex);
    if (g_bridge_class == nullptr) return false;

    const jmethodID method = env->GetStaticMethodID(g_bridge_class, name, signature);
    if (method == nullptr) {
        clear_exception(env);
        return false;
    }

    const jboolean result = env->CallStaticBooleanMethodA(g_bridge_class, method, arguments);
    if (env->ExceptionCheck()) {
        clear_exception(env);
        return false;
    }
    return result == JNI_TRUE;
}

jstring make_string(JNIEnv* env, const char* value) {
    return value == nullptr ? nullptr : env->NewStringUTF(value);
}

int call_string_int(const char* name, const char* signature, const char* value, int number) {
    JNIEnv* env = mobile_launcher_get_env();
    if (env == nullptr) return -1;

    jstring java_value = make_string(env, value);
    if (java_value == nullptr) {
        mobile_launcher_detach_current_thread();
        return -1;
    }

    jvalue arguments[2]{};
    arguments[0].l = java_value;
    arguments[1].i = number;
    const int result = call_int(env, name, signature, arguments);
    env->DeleteLocalRef(java_value);
    mobile_launcher_detach_current_thread();
    return result;
}

int call_string_two_int(
    const char* name,
    const char* signature,
    const char* value,
    int first,
    int second) {
    JNIEnv* env = mobile_launcher_get_env();
    if (env == nullptr) return -1;

    jstring java_value = make_string(env, value);
    if (java_value == nullptr) {
        mobile_launcher_detach_current_thread();
        return -1;
    }

    jvalue arguments[3]{};
    arguments[0].l = java_value;
    arguments[1].i = first;
    arguments[2].i = second;
    const int result = call_int(env, name, signature, arguments);
    env->DeleteLocalRef(java_value);
    mobile_launcher_detach_current_thread();
    return result;
}

int call_two_string_int(
    const char* name,
    const char* signature,
    const char* first,
    const char* second,
    int number) {
    JNIEnv* env = mobile_launcher_get_env();
    if (env == nullptr) return -1;

    jstring java_first = make_string(env, first);
    jstring java_second = make_string(env, second);
    if (java_first == nullptr || java_second == nullptr) {
        if (java_first != nullptr) env->DeleteLocalRef(java_first);
        if (java_second != nullptr) env->DeleteLocalRef(java_second);
        mobile_launcher_detach_current_thread();
        return -1;
    }

    jvalue arguments[3]{};
    arguments[0].l = java_first;
    arguments[1].l = java_second;
    arguments[2].i = number;
    const int result = call_int(env, name, signature, arguments);
    env->DeleteLocalRef(java_first);
    env->DeleteLocalRef(java_second);
    mobile_launcher_detach_current_thread();
    return result;
}


}

extern "C" JNIEXPORT void JNICALL
Java_app_com_tmlpocket_core_platform_NativeCoreClr_nativeRegisterBridgeActivity(
    JNIEnv* env,
    jclass,
    jobject activity) {
    std::lock_guard lock(g_bridge_mutex);

    if (g_activity != nullptr) {
        env->DeleteGlobalRef(g_activity);
        g_activity = nullptr;
    }
    if (g_bridge_class != nullptr) {
        env->DeleteGlobalRef(g_bridge_class);
        g_bridge_class = nullptr;
    }

    if (activity == nullptr) return;

    g_activity = env->NewGlobalRef(activity);
    const jclass local_class = env->FindClass("app/com/tmlpocket/core/platform/AndroidBridge");
    if (local_class != nullptr) {
        g_bridge_class = static_cast<jclass>(env->NewGlobalRef(local_class));
        env->DeleteLocalRef(local_class);
    }
    clear_exception(env);
}

extern "C" JNIEXPORT void JNICALL
Java_app_com_tmlpocket_core_platform_NativeCoreClr_nativeClearBridgeActivity(
    JNIEnv* env,
    jclass) {
    std::lock_guard lock(g_bridge_mutex);
    if (g_activity != nullptr) {
        env->DeleteGlobalRef(g_activity);
        g_activity = nullptr;
    }
    if (g_bridge_class != nullptr) {
        env->DeleteGlobalRef(g_bridge_class);
        g_bridge_class = nullptr;
    }
    g_queued_events.clear();
    g_back_requested.store(false);
    g_keyboard_requested.store(false);
}

extern "C" JNIEXPORT void JNICALL
Java_app_com_tmlpocket_core_platform_NativeCoreClr_nativePostAndroidEvent(
    JNIEnv* env,
    jclass,
    jint kind,
    jlong timestamp_nanos,
    jstring text,
    jint key_code,
    jboolean pressed,
    jint pointer_id,
    jfloat x,
    jfloat y,
    jint action,
    jboolean focused,
    jboolean paused,
    jint request_code,
    jboolean granted) {
    if (kind == TML_POCKET_ANDROID_EVENT_BACK) {
        g_back_requested.store(true);
        return;
    }
    if (kind == TML_POCKET_ANDROID_EVENT_KEYBOARD_REQUEST) {
        g_keyboard_requested.store(true);
        return;
    }
    std::string text_value;
    if (text != nullptr) {
        const char* chars = env->GetStringUTFChars(text, nullptr);
        if (chars != nullptr) {
            text_value = chars;
            env->ReleaseStringUTFChars(text, chars);
        }
    }

    std::lock_guard lock(g_bridge_mutex);
    if (g_queued_events.size() >= kMaxEvents) {
        g_queued_events.pop_front();
    }
    TmlPocketAndroidEvent event{
        .kind = kind,
        .action = action,
        .timestamp_nanos = timestamp_nanos,
        .key_code = key_code,
        .pointer_id = pointer_id,
        .x = x,
        .y = y,
        .pressed = pressed == JNI_TRUE ? 1 : 0,
        .focused = focused == JNI_TRUE ? 1 : 0,
        .paused = paused == JNI_TRUE ? 1 : 0,
        .request_code = request_code,
        .granted = granted == JNI_TRUE ? 1 : 0,
        .text = nullptr
    };
    g_queued_events.push_back(QueuedAndroidEvent{event, std::move(text_value)});
}

extern "C" int tml_pocket_bridge_open_url(const char* url) {
    return call_string_int("openUrl", "(Ljava/lang/String;)I", url, 0);
}

extern "C" const char* tml_pocket_bridge_get_clipboard_text() {
    JNIEnv* env = mobile_launcher_get_env();
    if (env == nullptr) return nullptr;

    std::lock_guard lock(g_bridge_mutex);
    g_last_clipboard_text.clear();
    if (g_bridge_class == nullptr) {
        mobile_launcher_detach_current_thread();
        return nullptr;
    }

    const jmethodID method = env->GetStaticMethodID(
        g_bridge_class,
        "getClipboardText",
        "()Ljava/lang/String;");
    if (method == nullptr) {
        clear_exception(env);
        mobile_launcher_detach_current_thread();
        return nullptr;
    }

    const auto value = static_cast<jstring>(env->CallStaticObjectMethod(g_bridge_class, method));
    if (env->ExceptionCheck()) {
        clear_exception(env);
    } else if (value != nullptr) {
        const char* chars = env->GetStringUTFChars(value, nullptr);
        if (chars != nullptr) {
            g_last_clipboard_text = chars;
            env->ReleaseStringUTFChars(value, chars);
        }
        env->DeleteLocalRef(value);
    }
    mobile_launcher_detach_current_thread();
    return g_last_clipboard_text.empty() ? nullptr : g_last_clipboard_text.c_str();
}

extern "C" int tml_pocket_bridge_set_clipboard_text(const char* text) {
    return call_string_int("setClipboardText", "(Ljava/lang/String;)I", text, 0);
}

extern "C" int tml_pocket_bridge_set_orientation(int landscape) {
    JNIEnv* env = mobile_launcher_get_env();
    if (env == nullptr) return -1;
    jvalue arguments[1]{};
    arguments[0].i = landscape;
    const int result = call_int(env, "setOrientation", "(I)I", arguments);
    mobile_launcher_detach_current_thread();
    return result;
}

extern "C" std::int64_t tml_pocket_bridge_get_display_size() {
    JNIEnv* env = mobile_launcher_get_env();
    if (env == nullptr) return 0;
    const std::int64_t result = call_long(env, "getDisplaySize", "()J", nullptr);
    mobile_launcher_detach_current_thread();
    return result;
}

extern "C" int tml_pocket_bridge_set_fullscreen(int fullscreen) {
    JNIEnv* env = mobile_launcher_get_env();
    if (env == nullptr) return -1;
    jvalue arguments[1]{};
    arguments[0].i = fullscreen;
    const int result = call_int(env, "setFullscreen", "(I)I", arguments);
    mobile_launcher_detach_current_thread();
    return result;
}

extern "C" int tml_pocket_bridge_set_ime_visible(int visible) {
    JNIEnv* env = mobile_launcher_get_env();
    if (env == nullptr) return -1;
    jvalue arguments[1]{};
    arguments[0].i = visible;
    const int result = call_int(env, "setImeVisible", "(I)I", arguments);
    mobile_launcher_detach_current_thread();
    return result;
}

extern "C" int tml_pocket_bridge_show_toast(const char* message, int long_duration) {
    return call_string_int("showToast", "(Ljava/lang/String;I)I", message, long_duration);
}

extern "C" int tml_pocket_bridge_set_startup_status(
    const char* title,
    const char* detail,
    int progress_percent) {
    return call_two_string_int(
        "setStartupStatus",
        "(Ljava/lang/String;Ljava/lang/String;I)I",
        title,
        detail,
        progress_percent);
}

extern "C" void tml_pocket_bridge_report_frame_rate(int fps) {
    JNIEnv* env = mobile_launcher_get_env();
    if (env == nullptr) return;
    jvalue arguments[1]{};
    arguments[0].i = fps;
    call_int(env, "reportFrameRate", "(I)I", arguments);
    mobile_launcher_detach_current_thread();
}

extern "C" int tml_pocket_bridge_hide_startup_status() {
    JNIEnv* env = mobile_launcher_get_env();
    if (env == nullptr) return -1;
    const int result = call_int(env, "hideStartupStatus", "()I", nullptr);
    mobile_launcher_detach_current_thread();
    return result;
}

extern "C" int tml_pocket_bridge_has_permission(const char* permission) {
    JNIEnv* env = mobile_launcher_get_env();
    if (env == nullptr) return 0;
    jstring java_permission = make_string(env, permission);
    if (java_permission == nullptr) {
        mobile_launcher_detach_current_thread();
        return 0;
    }
    jvalue arguments[1]{};
    arguments[0].l = java_permission;
    const bool granted = call_bool(env, "hasPermission", "(Ljava/lang/String;)Z", arguments);
    env->DeleteLocalRef(java_permission);
    mobile_launcher_detach_current_thread();
    return granted ? 1 : 0;
}

extern "C" int tml_pocket_bridge_request_permission(const char* permission, int request_code) {
    return call_string_int("requestPermission", "(Ljava/lang/String;I)I", permission, request_code);
}

extern "C" int tml_pocket_bridge_begin_text_prompt(
    const char* title,
    int password,
    int request_code) {
    return call_string_two_int(
        "showTextPrompt",
        "(Ljava/lang/String;II)I",
        title,
        password,
        request_code);
}

extern "C" int tml_pocket_bridge_poll_event(TmlPocketAndroidEvent* event) {
    if (event == nullptr) return 0;
    std::lock_guard lock(g_bridge_mutex);
    if (g_queued_events.empty()) return 0;

    auto queued = std::move(g_queued_events.front());
    g_queued_events.pop_front();
    g_last_event_text = std::move(queued.text);
    *event = queued.event;
    event->text = g_last_event_text.empty() ? nullptr : g_last_event_text.c_str();
    return 1;
}

extern "C" int tml_pocket_bridge_take_back() {
    return g_back_requested.exchange(false) ? 1 : 0;
}

extern "C" int tml_pocket_bridge_take_keyboard_request() {
    return g_keyboard_requested.exchange(false) ? 1 : 0;
}
