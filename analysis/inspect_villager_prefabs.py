import sys,json
from pathlib import Path
sys.path.insert(0,str(Path(__file__).parent/'python_deps'))
import UnityPy
base=Path(r'F:\SteamLibrary\steamapps\common\Broforce\Broforce_beta_Data\StreamingAssets')
env=UnityPy.load(*[str(base/n) for n in ['networkobjects.assetbundle','sharedassets.assetbundle','sharedtextures.assetbundle','sharedsounds.assetbundle']])
from UnityPy.classes import PPtr
out=[]
def resolve(ptr, af):
    return PPtr(m_FileID=ptr['m_FileID'],m_PathID=ptr['m_PathID'],assetsfile=af).deref()
for name in ['villager1','villager2']:
    path=f'assets/bundleassets/networkobjects/villagers/{name}.prefab'
    obj=env.container[path].deref()
    tree=obj.read_typetree(); record={'path':path,'components':[]}
    for c in tree['m_Component']:
        o=resolve(c['component'],obj.assets_file)
        t=o.read_typetree(); label=o.type.name
        if 'm_Script' in t:
            try: label=resolve(t['m_Script'],o.assets_file).read_typetree().get('m_ClassName','?')
            except Exception as e: label=str(e)
        refs={}
        for k,v in t.items():
            if isinstance(v,dict) and 'm_PathID' in v and v['m_PathID']:
                try:
                    r=resolve(v,o.assets_file); rt=r.read_typetree()
                    refs[k]={'type':r.type.name,'name':rt.get('m_Name',''),'file':r.assets_file.name,'id':r.path_id}
                    if r.type.name=='Material': refs[k]['material']=rt
                except Exception as e: refs[k]={'error':str(e)}
        record['components'].append({'type':label,'tree':t,'refs':refs})
        print(name,label,json.dumps({k:v for k,v in t.items() if k in ['hasGun','playerNum','isHero']},ensure_ascii=False))
        print(json.dumps(refs,ensure_ascii=False)[:5000])
    out.append(record)
Path(__file__).with_name('villager_prefab_details.json').write_text(json.dumps(out,ensure_ascii=False,indent=2,default=str),encoding='utf-8')
