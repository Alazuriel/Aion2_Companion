# NFL 1.0 – Nexforge Language

NFL is a compressed binary container for Companion translations. It contains data only; loading an NFL file never executes code or downloads external resources. Translation editors work with the structured NFL document, while ordinary text editors cannot accidentally alter packaged language files.

## File structure

Inside the container, the canonical NFL document begins with:

```text
!NFL 1.0
```

Metadata follows the header and must appear before the first section:

```text
@language "hu-HU"
@name "Magyar"
@author "Community"
@fallback "en-US"
@product "aion2-companion"
```

`language` and `name` are required. `author`, `fallback` and `product` are optional. The editor uses `product` to reconnect a translation to its original-language catalog when keys need to be refreshed. Language codes use a BCP 47-style form such as `en-US`, `hu-HU` or `pt-BR`.

Translations are grouped into sections:

```text
[Navigation]
Home = "Főoldal"
Account = "Fiók"

[Messages]
FightSaved = "Harc elmentve: {name}"
```

Section and key names begin with an ASCII letter. Letters, digits, `_` and `-` are supported; keys may also contain `.`. A section or key may not occur twice.

## Strings and escapes

Translation values are always enclosed in double quotes. Supported escapes are:

| Escape | Meaning |
| --- | --- |
| `\\` | backslash |
| `\"` | double quote |
| `\n` | new line |
| `\r` | carriage return |
| `\t` | tab |
| `\uFFFF` | four-digit Unicode code unit |

Unknown or incomplete escape sequences are rejected.

## Comments

Blank lines are ignored. A line whose first non-whitespace character is `#` or `;` is a comment. Inline comments are deliberately not supported, so comment characters remain safe inside translations.

## Placeholders

Named and numeric placeholders are supported, for example `{name}`, `{count}` and `{0}`. A translation must preserve the placeholder set of the base text. Alignment and format suffixes such as `{0,8}` and `{0:N0}` are allowed.

NFL does not evaluate placeholders. The Companion inserts values only when the matching message is displayed.

## Binary container

Packaged `.nfl` files begin with the `NXFL` magic, container version `1`, zlib compression and a SHA-256 digest of the canonical document. The digest detects incomplete or manually damaged files. This is an integrity and packaging format, not secret-key encryption.

The Python and C# loaders can still read the earlier plain-text NFL 1.0 form so existing work is not lost. Both editors always save the binary container.

## Limits and error handling

- Maximum file size: 1 MiB.
- Maximum physical line length: 16,384 characters.
- Maximum translations: 10,000.
- Internal document encoding: strict UTF-8.
- Supported format version: exactly 1.0.

Invalid files are not activated. Parsers report a line and column when the source location is known. The Companion continues with its built-in language when a community file is missing or invalid.

## Deterministic serialization

Writers emit metadata in the order `language`, `name`, `author`, `fallback`, then any additional metadata alphabetically. Sections and keys are written alphabetically using LF line endings and UTF-8 without BOM. Parsing and serializing a canonical NFL file produces identical bytes.
