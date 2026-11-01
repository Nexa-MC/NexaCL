# Native Minecraft fixture physical paths

The macOS independent-runtime fixture used a temporary directory below `/var`,
which is a system symbolic link. Full launch correctly rejected that ancestor
when acquiring its game-directory lease. The fixture now resolves existing
temporary-directory components before creating its unique test root.

Production game-directory link rejection remains unchanged. The same fixture
continues to exercise real Java/process launch, post-exit work, cancellation and
physical shutdown. Linux independent NativeAOT execution passes; macOS execution
is verified by the existing native-engine CI matrix.
