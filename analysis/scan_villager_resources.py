import sys, json
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent / 'python_deps'))
import UnityPy
base=Path(r'F:\SteamLibrary\steamapps\common\Broforce\Broforce_beta_Data')
results=[]
for name in ['resources.assets','sharedassets0.assets','StreamingAssets/networkobjects.assetbundle','StreamingAssets/themeholders.assetbundle','StreamingAssets/sharedassets.assetbundle']:
    env=UnityPy.load(str(base/name))
    containers=[{'path': k, 'file':v.assetsfile.name, 'id':v.path_id} for k,v in env.container.items() if any(x in k.lower() for x in ['villag','civilian','citizen'])]
    matches=[]
    for o in env.objects:
        if o.type.name not in ['GameObject','MonoBehaviour','Material','Texture2D','TextAsset']: continue
        try:
            tree=o.read_typetree()
            n=tree.get('m_Name','')
            if any(x in n.lower() for x in ['villag','civilian','citizen']) or (o.type.name=='TextAsset' and any(x in n.lower() for x in ['manifest','resource'])):
                matches.append({'type':o.type.name,'name':n,'file':o.assets_file.name,'id':o.path_id,'tree':tree})
        except Exception as e: pass
    results.append({'bundle':name,'containers':containers,'matches':matches})
    print(name, json.dumps(containers,ensure_ascii=False))
    print([(m['type'],m['name'],m['id']) for m in matches])
Path(__file__).with_name('villager_resource_scan.json').write_text(json.dumps(results,ensure_ascii=False,indent=2,default=str),encoding='utf-8')
