#!/bin/bash
# Export release builds for Linux and Windows into build/<platform>/, add the licence files, zip them into build/dist/.
# Usage: tools/build.sh            (needs the Godot 4.7.2 mono export templates in ~/.local/share/godot/export_templates/4.7.2.stable.mono/)
set -euo pipefail
cd "$(dirname "$0")/.."
version=$(grep '^config/version' project.godot | cut -d'"' -f2)
dotnet build Perigee.csproj -v q
rm -rf build/linux build/windows build/dist
mkdir -p build/linux build/windows build/dist
godot --headless --path . --export-release "Linux" build/linux/PerigeeFuelCo.x86_64
godot --headless --path . --export-release "Windows" build/windows/PerigeeFuelCo.exe
for d in build/linux build/windows; do
  mkdir -p "$d/licences"
  cp assets/fonts/Noto-OFL.txt docs/CREDITS.md "$d/licences/"
  [ -f docs/third-party/GODOT-LICENSE.txt ] && cp docs/third-party/*.txt "$d/licences/" || true
done
python3 -c "import shutil,sys; shutil.make_archive(sys.argv[1], 'zip', sys.argv[2])" "build/dist/PerigeeFuelCo-$version-linux-x86_64" build/linux
python3 -c "import shutil,sys; shutil.make_archive(sys.argv[1], 'zip', sys.argv[2])" "build/dist/PerigeeFuelCo-$version-windows-x86_64" build/windows
ls -la build/linux build/windows build/dist
