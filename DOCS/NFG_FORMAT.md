# Nexforge ForgeData container

The current ForgeData container is **NXFG v2**. It is intended to keep the
curated data packs out of casual inspection and to reject modified or damaged
files before parsing them.

## Security model

- Canonical JSON is compressed with zlib.
- The compressed payload is encrypted with AES-256-GCM.
- Every file receives a fresh 16-byte HKDF-SHA-256 salt and 12-byte GCM nonce.
- The complete non-secret header is authenticated as associated data.
- The 32-byte master key is never committed to Git.
- Official application builds embed the private key from
  `Private/NfgMaster.key`; local development can instead use the
  `NEXFORGE_NFG_MASTER_KEY` environment variable.

The encryption protects an `.nfg` file when the key is unavailable. It cannot
make data mathematically unrecoverable from an official executable that must
contain enough information to decrypt it. Obfuscation and signing can increase
the reverse-engineering cost later, but they do not change that trust boundary.

## Compatibility and migration

The reader accepts both formats:

- **NXFG v1:** zlib plus SHA-256 integrity only; not encrypted.
- **NXFG v2:** zlib plus authenticated AES-256-GCM encryption.

ForgeData Studio detects an opened v1 file and offers an in-place v2
conversion. Before overwriting it, the Studio creates a `.v1.bak` copy. Every
newly created or normally saved `.nfg` file uses v2.

## Key handling

`Private/NfgMaster.key` is ignored by Git and must contain exactly 32 random
bytes (Base64 or hexadecimal text is accepted as well). Back it up separately:
losing it makes existing v2 files unrecoverable, while leaking it compromises
every data pack created with it.
