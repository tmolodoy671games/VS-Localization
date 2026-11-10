from pathlib import Path
import shutil
root = Path(__file__).resolve().parent.parent
source = root / 'Source~/VS.Localization.Generator/bin/Release/netstandard2.0/VS.Localization.Generator.dll'
destination = root / 'Compiler~/VS.Localization.Generator.dll'
destination.parent.mkdir(parents=True, exist_ok=True)
shutil.copy2(source, destination)
shutil.copy2(source, root / "VS.Localization.Generator.dll")
print(destination)
