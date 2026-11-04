# Native update fixtures

Each RID directory contains test-only public keys, manifests, and signatures for three-byte archives. These archives cannot launch the product. Temporary fixture private keys were discarded; test fingerprints are separate from production publisher trust.

Fixed signature bytes let CI platforms exercise portable transactions without GPG or an external signer. Elevated runners verify production rejection policy. Never add fixture public keys to production trust.
