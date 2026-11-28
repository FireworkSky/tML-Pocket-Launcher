#pragma once

#include <cstdint>

enum TmlPocketAndroidEventKind : std::int32_t {
    TML_POCKET_ANDROID_EVENT_LIFECYCLE = 0,
    TML_POCKET_ANDROID_EVENT_TEXT = 1,
    TML_POCKET_ANDROID_EVENT_KEY = 2,
    TML_POCKET_ANDROID_EVENT_TOUCH = 3,
    TML_POCKET_ANDROID_EVENT_FOCUS = 4,
    TML_POCKET_ANDROID_EVENT_BACK = 5,
    TML_POCKET_ANDROID_EVENT_PERMISSION = 6,
    TML_POCKET_ANDROID_EVENT_LAUNCHER_ACTION = 7,
    TML_POCKET_ANDROID_EVENT_IME_COMMIT = 8,
    TML_POCKET_ANDROID_EVENT_IME_COMPOSITION = 9,
    TML_POCKET_ANDROID_EVENT_IME_VISIBILITY = 10,
    TML_POCKET_ANDROID_EVENT_KEYBOARD_REQUEST = 11
};

struct TmlPocketAndroidEvent {
    std::int32_t kind;
    // Key, touch, and launcher-action events share this existing action slot.
    std::int32_t action;
    std::int64_t timestamp_nanos;
    std::int32_t key_code;
    std::int32_t pointer_id;
    float x;
    float y;
    std::int32_t pressed;
    std::int32_t focused;
    std::int32_t paused;
    std::int32_t request_code;
    std::int32_t granted;
    const char* text;
};

extern "C" {

int tml_pocket_bridge_open_url(const char* url);
const char* tml_pocket_bridge_get_clipboard_text();
int tml_pocket_bridge_set_clipboard_text(const char* text);
int tml_pocket_bridge_set_orientation(int landscape);
std::int64_t tml_pocket_bridge_get_display_size();
int tml_pocket_bridge_set_fullscreen(int fullscreen);
int tml_pocket_bridge_set_ime_visible(int visible);
int tml_pocket_bridge_show_toast(const char* message, int long_duration);
int tml_pocket_bridge_set_startup_status(const char* title, const char* detail, int progress_percent);
int tml_pocket_bridge_hide_startup_status();
void tml_pocket_bridge_report_frame_rate(int fps);
int tml_pocket_bridge_has_permission(const char* permission);
int tml_pocket_bridge_request_permission(const char* permission, int request_code);
int tml_pocket_bridge_begin_text_prompt(const char* title, int password, int request_code);
int tml_pocket_bridge_poll_event(TmlPocketAndroidEvent* event);
int tml_pocket_bridge_take_back();
int tml_pocket_bridge_take_keyboard_request();

}
