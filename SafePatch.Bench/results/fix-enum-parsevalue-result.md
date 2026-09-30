# Return the parsed value from `EnumBinaryTranslation.ParseValue(reader)`

Branch `fix/enum-parsevalue-result` (one commit on 0.54.4).

`ParseValue(TReader)` read and converted the value and then returned `default(TEnum)` for every input. Nothing in
Mutagen's record code calls it (only a benchmark in `Mutagen.Bethesda.Tests`), so reading plugins is unaffected
and there is no performance effect to measure; the fix is for callers of the public API. It now reads through the
same path as `Parse(reader, length)`.

## Tests

`EnumBinaryTranslationParseValueTests`: one-, two- and four-byte values read back as `Value.Two` (all fail before
the fix, returning `None`).

It conflicts textually with `fix/unsigned-two-byte-enums` (both touch the two-byte case); merged, `ParseValue`
delegates to the unsigned read.
