#!/bin/bash
# Runs the unit tests through scripts/XCTestShim.swift (the Monterey dev Mac has only the Command Line Tools: no XCTest).
# With full Xcode the same files run natively under XCTest.
set -euo pipefail
cd "$(dirname "$0")/.."
OUT=build/tests; rm -rf "$OUT"; mkdir -p "$OUT/src"
for f in $(find Sources/ADMac -name '*.swift' ! -path '*/App/*'); do cp "$f" "$OUT/src/$(basename "$f")"; done
for f in $(find Tests/ADMacTests -name '*.swift'); do
  sed -e '/@testable import/d' -e 's/^import XCTest$/import Foundation/' "$f" > "$OUT/src/$(basename "$f")"
done
cp scripts/XCTestShim.swift "$OUT/src/XCTestShim.swift"
CLASSES=$(grep -hoE 'class [A-Za-z0-9_]+: XCTestCase' Tests/ADMacTests/*.swift | awk '{sub(":","",$2); printf "%s.self, ", $2}')
printf 'import Foundation\n@main struct TestMain { static func main() { exit(runAllShimTests([%s])) } }\n' "$CLASSES" > "$OUT/src/TestMain.swift"
swiftc -O -parse-as-library -module-name ADMacTests -target "$(uname -m)-apple-macos12.0" "$OUT"/src/*.swift -o "$OUT/run-tests"
ADMAC_RESOURCES="$PWD/Resources" "$OUT/run-tests"
