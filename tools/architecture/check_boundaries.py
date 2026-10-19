"""Fast dependency guard. Build-only host references are deployment edges, not UI implementation dependencies."""
from pathlib import Path
import xml.etree.ElementTree as ET
ROOT=Path(__file__).resolve().parents[2]
projects={p.stem:p for p in (ROOT/'src').glob('*/*.csproj')}
graph={}
for name,path in projects.items():
 refs=[]
 for item in ET.parse(path).getroot().iter('ProjectReference'):
  if item.get('ReferenceOutputAssembly','true').lower()=='false':continue
  refs.append(Path(item.get('Include').replace('\\','/')).stem)
 graph[name]=refs
def reachable(name):
 seen=set();todo=list(graph[name])
 while todo:
  current=todo.pop()
  if current in seen:continue
  seen.add(current);todo.extend(graph.get(current,[]))
 return seen
rules={
 'Echo.Desktop':{'Echo.Core','Echo.Core.Host','Echo.Bridge','Echo.Bridge.Host','Echo.Audio.Client','Echo.Playback'},
 'Echo.Bridge.Host':{'Echo.Core','Echo.Core.Host','Echo.Infrastructure','Echo.Desktop','Echo.Audio.Client','Echo.Playback'},
 'Echo.Core.Host':{'Echo.Desktop','Echo.Visuals'},
 'Echo.Core.Contracts':{'Echo.Core','Echo.Core.Client','Echo.Infrastructure','Echo.Application','Echo.Platform','Echo.Desktop','Echo.Playback'},
 'Echo.Application':{'Echo.Core','Echo.Infrastructure','Echo.Desktop','Echo.Platform','Echo.Audio.Client','Echo.Playback'},
 'Echo.Playback':{'Echo.Desktop','Echo.Infrastructure','Echo.Platform','Echo.Audio.Client','Echo.Core'},
}
for name,forbidden in rules.items():
 overlap=reachable(name)&forbidden
 if overlap:raise SystemExit(f'Forbidden dependency: {name} -> {sorted(overlap)}')
print('PASS dependency boundaries: Control, Core, Bridge, playback, and contracts')
