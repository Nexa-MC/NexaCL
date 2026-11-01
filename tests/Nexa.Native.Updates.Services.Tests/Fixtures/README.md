# Inert signed native Updates fixtures

Each RID directory contains a generated test-only public key and detached signature
for the manifest and three-byte archive members. These packages cannot execute a
launcher. The temporary RSA signing keys were discarded and are not production
release credentials. Tests explicitly pin each fixture fingerprint; production
continues pinning the existing embedded release key.

The fixtures are checked in so all supported CI platforms run the same signed bytes
without a GPG executable or external signing service. Ordinary-user runs exercise
portable transactions; elevated runners assert the production rejection policy.
