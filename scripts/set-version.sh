#!/usr/bin/env bash
#
# Set the version of the integrations and generate their NuGet packages. Nothing is uploaded.
#
# The version lives in one place, <Version> in Directory.Build.props. A packable project is a .csproj
# with <IsPackable>true</IsPackable>, because Directory.Build.props turns packing off by default.
#
# Usage:
#   scripts/set-version.sh 0.2.0                  # rewrite <Version>, then pack
#   scripts/set-version.sh 0.2.0 --no-pack        # only rewrite <Version>
#   scripts/set-version.sh 0.2.0 --dry-run        # show what would change, touch nothing
#   scripts/set-version.sh 0.2.0 -o /tmp/nupkg    # pack into a different output directory
#   scripts/set-version.sh 0.2.0 -c Debug         # pack a non-Release configuration
#
# Packages land in <repo>/nupkg by default.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

PROPS="$REPO_ROOT/Directory.Build.props"

VERSION=""
CONFIGURATION="Release"
OUTPUT="$REPO_ROOT/nupkg"
PACK=1
DRY_RUN=0

usage() {
  sed -n '2,15p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
  exit "${1:-0}"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help)          usage 0 ;;
    --no-pack)          PACK=0; shift ;;
    --dry-run)          DRY_RUN=1; shift ;;
    -c|--configuration) CONFIGURATION="$2"; shift 2 ;;
    -o|--output)        OUTPUT="$2"; shift 2 ;;
    -*)                 echo "error: unknown option: $1" >&2; usage 1 ;;
    *)
      if [[ -n "$VERSION" ]]; then
        echo "error: unexpected argument: $1" >&2
        usage 1
      fi
      VERSION="$1"; shift ;;
  esac
done

if [[ -z "$VERSION" ]]; then
  echo "error: no version given" >&2
  usage 1
fi

# Only forms that NuGet keeps as written, so the file name of each package can be predicted below.
# NuGet would rename 1.2.3.0 to 1.2.3, and it drops +build metadata from the file name.
if [[ ! "$VERSION" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$ ]]; then
  echo "error: '$VERSION' is not a valid version (expected e.g. 1.2.0, 1.2.0-rc.1)" >&2
  exit 1
fi

# Discover packable projects instead of hardcoding a list, so a new package cannot be forgotten.
PACKABLE=()
SKIPPED=()
while IFS= read -r project; do
  if grep -qE '<IsPackable>[[:space:]]*true[[:space:]]*</IsPackable>' "$project"; then
    PACKABLE+=("$project")
  else
    SKIPPED+=("$project")
  fi
done < <(find "$REPO_ROOT" -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' | sort)

if [[ ${#PACKABLE[@]} -eq 0 ]]; then
  echo "error: no packable projects found under $REPO_ROOT" >&2
  exit 1
fi

current="$(grep -oE '<Version>[^<]+</Version>' "$PROPS" | head -n1 | sed -E 's#</?Version>##g')"

if [[ -z "$current" ]]; then
  echo "error: no <Version> in $PROPS" >&2
  exit 1
fi

echo "Version:       $current -> $VERSION"
echo "Configuration: $CONFIGURATION"
echo "Output:        $OUTPUT"
echo "Pack:          $PACK"
echo
echo "Packable:"
for project in "${PACKABLE[@]}"; do
  echo "  ${project#"$REPO_ROOT"/}"
done

if [[ ${#SKIPPED[@]} -gt 0 ]]; then
  echo
  echo "Skipped (not packable):"
  for project in "${SKIPPED[@]}"; do
    echo "  ${project#"$REPO_ROOT"/}"
  done
fi

if [[ "$DRY_RUN" -eq 1 ]]; then
  echo
  echo "Dry run: nothing written."
  exit 0
fi

# Replace only the first <Version>. The other version properties have different element names.
VERSION="$VERSION" perl -0777 -pi -e 's{<Version>[^<]*</Version>}{"<Version>$ENV{VERSION}</Version>"}se' "$PROPS"

written="$(grep -oE '<Version>[^<]+</Version>' "$PROPS" | head -n1 | sed -E 's#</?Version>##g')"
if [[ "$written" != "$VERSION" ]]; then
  echo "error: failed to write <Version> into $PROPS (found '${written:-<none>}')" >&2
  exit 1
fi

if [[ "$PACK" -ne 1 ]]; then
  echo
  echo "Skipping pack."
  exit 0
fi

mkdir -p "$OUTPUT"

echo
echo "Packing ${#PACKABLE[@]} project(s)..."

PACKED=()
for project in "${PACKABLE[@]}"; do
  package_id="$(grep -oE '<PackageId>[^<]+</PackageId>' "$project" | head -n1 | sed -E 's#</?PackageId>##g')"
  expected="$OUTPUT/$package_id.$VERSION.nupkg"

  # Drop any same-version leftover so a stale package can't be mistaken for a fresh one.
  rm -f "$expected"

  echo
  echo "==> $package_id"
  # UseLocalKahuna=false: a package must depend on the released Kahuna.Client, never on a local checkout.
  dotnet pack "$project" -c "$CONFIGURATION" -o "$OUTPUT" -p:UseLocalKahuna=false

  if [[ ! -f "$expected" ]]; then
    echo "error: expected package not produced: $expected" >&2
    exit 1
  fi
  PACKED+=("$expected")
done

echo
echo "Packed ${#PACKED[@]} package(s) into $OUTPUT:"
for package in "${PACKED[@]}"; do
  printf '  %-52s %s\n' "$(basename "$package")" "$(du -h "$package" | cut -f1)"
done
