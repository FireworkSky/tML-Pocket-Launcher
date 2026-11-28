#include <jni.h>
#include <string.h>
// stb_dxt v1.13, upstream revision 48fb1c9709f973041a7e6992d720ac625266cdd3.
#define STB_DXT_IMPLEMENTATION
#include "vendor/stb_dxt.h"

JNIEXPORT void JNICALL Java_app_com_tmlpocket_feature_mods_TmodTextures_encodeBc3Strip(
    JNIEnv *env, jclass type, jbyteArray pixels, jbyteArray output, jint width)
{
    unsigned char blocks[16384 * 4];
    unsigned char tile[64];
    (void)type;
    if (width <= 0 || width > 16384 || width % 4 ||
        (*env)->GetArrayLength(env, pixels) < width * 16 ||
        (*env)->GetArrayLength(env, output) < width * 4) {
        (*env)->ThrowNew(env, (*env)->FindClass(env, "java/lang/IllegalArgumentException"), "Invalid BC3 strip");
        return;
    }
    const jbyte *rgba = (*env)->GetPrimitiveArrayCritical(env, pixels, NULL);
    if (rgba == NULL) return;
    for (int x = 0; x < width; x += 4) {
        for (int y = 0; y < 4; ++y)
            memcpy(tile + y * 16, rgba + (y * width + x) * 4, 16);
        stb_compress_dxt_block(blocks + x * 4, tile, 1, STB_DXT_HIGHQUAL);
    }
    (*env)->ReleasePrimitiveArrayCritical(env, pixels, (void *)rgba, JNI_ABORT);
    (*env)->SetByteArrayRegion(env, output, 0, width * 4, (const jbyte *)blocks);
}
