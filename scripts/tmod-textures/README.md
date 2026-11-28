# tModLoader 大贴图转换器（Windows x64）

需要 64 位 Java 11 或更高版本。解压工具包，在 JAR 所在目录打开终端。输入一个路径可以查看预计节省量，追加一个新的输出路径会生成独立的本地模组包。输出目录需已存在。

```powershell
java -jar tmod-textures.jar --bc3 "D:\Mods\Example.tmod"
java -jar tmod-textures.jar --bc3 "D:\Mods\Example.tmod" "D:\Test\Example.tmod"
java -jar tmod-textures.jar "D:\Mods\Example.tmod" "D:\Test16\Example.tmod"
```

默认输出 BGRA4444，每像素 2 字节。可选的 `--bc3` 模式需要 JAR 同目录的 `tmod_bc3.dll`，按 4×4 块编码颜色和透明度，每块 16 字节。BC3 可能让像素画出现色块，BGRA4444 可能让渐变出现色阶。

工具处理至少 65,536 像素、且目标 XNB 路径可用的 RAWIMG。BC3 模式将宽高为 4 的倍数且宽不超过 16,384 的贴图编码为 DXT5，其余选中贴图使用 16 位。两种格式均保留原图宽高及动画帧布局。其他成员按原有压缩字节复制。程序验证输入与输出包的 SHA-1，生成无签名的本地包。联机双方需注意模组哈希的一致性。

灾厄 2.2.4 加载后会从 BlobbyNoise 等原图逐像素读取 32 位颜色。两种转换模式都为已知的灰度和图块、墙体遮罩回读路径保留 RAWIMG。已用 Windows D3D11 客户端验证灾厄加载。修复旧包时请从原版模组重新转换。

其他模组若按扩展名直接读取 RAWIMG，或使用原来的 32 位布局读写纹理，需要单独验证。当前 FNA 的 D3D11 后端对 BC3 调用 `GetData` 会报错。设备缺少 S3TC 支持时，FNA 会在加载 DXT5 时解回 32 位。Android 的节省量需要在目标设备上测量。

## 从源码构建

设置 `JAVA_HOME` 指向带 JNI 头文件的 x64 JDK，安装含 x64 C++ 工具的 Visual Studio 或 Build Tools，运行本目录的 `build.cmd`。输出在仓库的 `build/tmod-textures`。

BC3 编码器使用固定版本的 [stb_dxt.h](https://github.com/nothings/stb/blob/48fb1c9709f973041a7e6992d720ac625266cdd3/stb_dxt.h)，授权文本见该文件末尾。
