import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

function apiRoot(baseUrl) {
  let base=String(baseUrl||'').trim().replace(/\/+$/,'');
  for(const suffix of ['/chat/completions','/images/generations','/images/edits','/models']) if(base.endsWith(suffix)) base=base.slice(0,-suffix.length);
  if(!base) throw new Error('请填写 Base URL');
  // 严格使用用户填写的地址；不同网络的 ToApis 可用域名并不相同，
  // 不能在客户端擅自把 .com 改写为 .xyz，否则会造成 UND_ERR_SOCKET。
  return base;
}

async function checked(response,label) {
  const text=await response.text();
  if(!response.ok) throw new Error(`${label} HTTP ${response.status}: ${text.slice(0,800)}`);
  try{return JSON.parse(text)}catch{throw new Error(`${label} 未返回 JSON: ${text.slice(0,500)}`)}
}

async function models(config,key) {
  const payload=await checked(await fetch(apiRoot(config.base_url)+'/models',{headers:{Authorization:`Bearer ${key}`},signal:AbortSignal.timeout(120000)}),'模型列表');
  const source=Array.isArray(payload?.data)?payload.data:Array.isArray(payload?.models)?payload.models:[];
  const list=source.map(x=>typeof x==='string'?x:x?.id||x?.name||x?.model).filter(Boolean);
  if(!list.length) throw new Error(`接口没有返回 data[].id 或 models[]；返回字段：${Object.keys(payload||{}).join(', ')}。仍可手动填写模型名`);
  return {ok:true,models:[...new Set(list)].sort()};
}

function mime(file) {
  const ext=path.extname(file).toLowerCase();
  return ext==='.jpg'||ext==='.jpeg'?'image/jpeg':ext==='.webp'?'image/webp':'image/png';
}

function selectedImageQuality(config) {
  const quality=String(config?.quality||'auto').toLowerCase();
  return ['auto','low','medium','high'].includes(quality)?quality:'auto';
}

function selectedImageSize(config) {
  const value=String(config?.size||'auto').trim().toLowerCase().replace(/×/g,'x').replace(/\s+/g,'');
  return value==='auto'||/^\d+x\d+$/.test(value)?value:'auto';
}

function proxyImageShape(config) {
  const size=selectedImageSize(config);
  if(size==='auto')return {aspect:String(config.aspect_ratio||'auto').trim()||'auto',resolution:String(config.resolution||'auto').trim()||'auto'};
  const [width,height]=size.split('x').map(Number);let a=width,b=height;while(b){const next=a%b;a=b;b=next;}
  return {aspect:`${width/a}:${height/a}`,resolution:Math.max(width,height)>=3000?'4K':Math.max(width,height)>=1800?'2K':'1K'};
}

// ToApis 的普通图像通道通常将 size 作为可选宽高比，省略后采用模型默认值；
// 标有 official 的官方通道额外接受 size="auto"。具体比例仍折算到文档支持的集合。
const TOAPIS_ASPECTS=[[1,1],[3,2],[2,3],[4,3],[3,4],[5,4],[4,5],[16,9],[9,16],[2,1],[1,2],[21,9],[9,21]];

function snapToApisAspect(value) {
  const match=String(value||'').match(/^\s*(\d+)\s*:\s*(\d+)\s*$/);
  if(!match) return '1:1';
  const width=Number(match[1]),height=Number(match[2]);
  if(!width||!height) return '1:1';
  const exact=TOAPIS_ASPECTS.find(([w,h])=>w===width&&h===height);
  if(exact) return width+':'+height;
  const target=width/height;
  let best=TOAPIS_ASPECTS[0],bestDiff=Infinity;
  for(const [w,h] of TOAPIS_ASPECTS){const diff=Math.abs(w/h-target);if(diff<bestDiff){bestDiff=diff;best=[w,h];}}
  return best[0]+':'+best[1];
}

function toApisImageSize(config,shape) {
  const requested=String(shape.aspect||config.aspect_ratio||'auto').trim().toLowerCase();
  return requested==='auto'?(/official/i.test(String(config.model||''))?'auto':''):snapToApisAspect(requested);
}

// GPT Image 类模型只支持 /images/generations，从不支持 /chat/completions。
// 首选失败后回退到对话接口对这类模型必然 400，只会掩盖真正原因，应直接跳过回退。
function supportsChatImageFallback(model) {
  return !isGptImageModel(model);
}

function isGptImageModel(model) {
  return /^gpt-image(?:-|$)/i.test(String(model||'').trim());
}

function isSeedreamModel(model) {
  return /seedream/i.test(String(model||'').trim());
}

// ToApis 的 Seedream 5.0 / 5.0 Pro 将高级通道参数置于 metadata。
// 不能依赖服务端默认值：不同通道的默认水印策略并不一致。
function applySeedreamNoWatermark(body, resolution) {
  body.metadata={...(body.metadata||{}),watermark:false};
  if(resolution&&String(resolution).toLowerCase()!=='auto') body.metadata.resolution=resolution;
}

async function ocr(config) {
  const bytes=await readFile(config.image_path); const data=`data:${mime(config.image_path)};base64,${bytes.toString('base64')}`;
  const body={model:config.model,messages:[{role:'user',content:[
    {type:'text',text:'完整识别图片中所有可见文字。保持原有顺序和换行，只输出识别文字，不要解释；无法辨认处写[不清楚]。'},
    {type:'image_url',image_url:{url:data}}
  ]}]};
  const payload=await checked(await fetch(apiRoot(config.base_url)+'/chat/completions',{method:'POST',headers:{Authorization:`Bearer ${process.env.TEXT_LLM_API_KEY}`,'Content-Type':'application/json'},body:JSON.stringify(body),signal:AbortSignal.timeout(180000)}),'文字识别');
  const text=payload?.choices?.[0]?.message?.content;
  if(typeof text!=='string'||!text.trim()) throw new Error('文本模型没有返回识别文字');
  return {ok:true,text:text.trim()};
}

async function ocrLayout(config) {
  const bytes=await readFile(config.image_path);const data=`data:${mime(config.image_path)};base64,${bytes.toString('base64')}`;
  const instruction='识别图片中所有可见文字并返回严格 JSON，不要 Markdown。格式：{"blocks":[{"text":"文字","x":0.1,"y":0.2,"width":0.3,"height":0.05}]}。坐标和宽高均为图片左上角起算的 0 到 1 比例；每行或相邻短语一个 block，尽量准确覆盖原文字位置。';
  const body={model:config.model,messages:[{role:'user',content:[{type:'text',text:instruction},{type:'image_url',image_url:{url:data}}]}]};
  const payload=await checked(await fetch(apiRoot(config.base_url)+'/chat/completions',{method:'POST',headers:{Authorization:`Bearer ${process.env.TEXT_LLM_API_KEY}`,'Content-Type':'application/json'},body:JSON.stringify(body),signal:AbortSignal.timeout(240000)}),'图片文字定位');
  let content=payload?.choices?.[0]?.message?.content;if(Array.isArray(content))content=content.map(x=>x?.text||'').join('');if(typeof content!=='string')throw new Error('文本模型没有返回文字定位结果');
  const cleaned=content.replace(/^```(?:json)?\s*/i,'').replace(/\s*```$/,'').trim();let parsed;try{parsed=JSON.parse(cleaned)}catch{const start=cleaned.indexOf('{'),end=cleaned.lastIndexOf('}');if(start<0||end<=start)throw new Error('文字定位结果不是有效 JSON');parsed=JSON.parse(cleaned.slice(start,end+1));}
  const blocks=Array.isArray(parsed?.blocks)?parsed.blocks.filter(x=>x&&typeof x.text==='string'&&x.text.trim()).map(x=>({text:x.text.trim(),x:Number(x.x)||0,y:Number(x.y)||0,width:Number(x.width)||0.2,height:Number(x.height)||0.05})):[];
  if(!blocks.length)throw new Error('没有识别到可定位的文字');return {ok:true,blocks,text:blocks.map(x=>x.text).join('\n')};
}

// 视觉文本模型负责把“拆分出的透明元素”重新映射回原图坐标系。
// index 从 1 开始，对应传入 layers 数组的顺序；所有坐标均为原图宽高的 0~1 比例。
async function layerLayout(config) {
  const paths=Array.isArray(config.image_paths)?config.image_paths:[];
  if(paths.length<2) throw new Error('图层智能定位至少需要原图和一个图层');
  const content=[{type:'text',text:'你是图层排版助手。第一张是完整原图，后续每张是从原图拆出的带透明通道元素。请判断每个元素在原图中的真实位置、大小和前后层级。只返回严格 JSON，不要 Markdown：{"layers":[{"index":1,"x":0.1,"y":0.2,"width":0.3,"height":0.4,"z":1}]}。index 对应后续图片顺序（第一张图层为 1）；x/y/width/height 都是相对于原图宽高的 0~1 数值，x/y 是图层外框左上角，z 越大越靠前。请让所有图层叠放后的构图尽量与第一张原图完全一致。'}];
  for(let i=0;i<paths.length;i++){const bytes=await readFile(paths[i]);content.push({type:'text',text:i===0?'原图（坐标系）':`图层 ${i}（透明 PNG）`});content.push({type:'image_url',image_url:{url:`data:${mime(paths[i])};base64,${bytes.toString('base64')}`}});}
  const body={model:config.model,messages:[{role:'user',content}]};
  const payload=await checked(await fetch(apiRoot(config.base_url)+'/chat/completions',{method:'POST',headers:{Authorization:`Bearer ${process.env.TEXT_LLM_API_KEY}`,'Content-Type':'application/json'},body:JSON.stringify(body),signal:AbortSignal.timeout(300000)}),'图层智能定位');
  let contentText=payload?.choices?.[0]?.message?.content;if(Array.isArray(contentText))contentText=contentText.map(x=>x?.text||'').join('');if(typeof contentText!=='string')throw new Error('视觉文本模型没有返回图层坐标');
  const cleaned=contentText.replace(/^```(?:json)?\s*/i,'').replace(/\s*```$/,'').trim();let parsed;try{parsed=JSON.parse(cleaned)}catch{const start=cleaned.indexOf('{'),end=cleaned.lastIndexOf('}');if(start<0||end<=start)throw new Error('图层智能定位结果不是有效 JSON');parsed=JSON.parse(cleaned.slice(start,end+1));}
  const layers=Array.isArray(parsed?.layers)?parsed.layers.map(x=>({index:Number(x.index),x:Number(x.x),y:Number(x.y),width:Number(x.width),height:Number(x.height),z:Number(x.z)})).filter(x=>Number.isFinite(x.index)&&Number.isFinite(x.x)&&Number.isFinite(x.y)&&Number.isFinite(x.width)&&Number.isFinite(x.height)):[];
  if(!layers.length)throw new Error('视觉文本模型没有给出有效图层坐标');return {ok:true,layers};
}

async function chat(config) {
  const messages=Array.isArray(config.messages)?config.messages:[];
  if(!messages.length) throw new Error('对话内容为空');
  // Some OpenAI reasoning models reject any non-default temperature value.
  // Omitting it lets every model use its supported default.
  const body={model:config.model,messages};
  const payload=await checked(await fetch(apiRoot(config.base_url)+'/chat/completions',{method:'POST',headers:{Authorization:`Bearer ${process.env.TEXT_LLM_API_KEY}`,'Content-Type':'application/json'},body:JSON.stringify(body),signal:AbortSignal.timeout(300000)}),'AI 对话');
  const content=payload?.choices?.[0]?.message?.content;
  const text=typeof content==='string'?content:(Array.isArray(content)?content.map(x=>x?.text||'').join(''):null);
  if(!text?.trim()) throw new Error('文本模型没有返回对话内容');
  return {ok:true,text:text.trim()};
}

async function audit(config) {
  const root=apiRoot(config.base_url); const key=process.env.TEXT_LLM_API_KEY;
  const content=[{type:'text',text:`${config.instructions||''}\n\n待审核文案：\n${config.content||'（无额外文字，请仅审核图片内容）'}`}];
  if(config.image_path){const bytes=await readFile(config.image_path);content.push({type:'image_url',image_url:{url:`data:${mime(config.image_path)};base64,${bytes.toString('base64')}`}});}
  const body={model:config.model,messages:[{role:'user',content}]};
  const payload=await checked(await fetch(root+'/chat/completions',{method:'POST',headers:{Authorization:`Bearer ${key}`,'Content-Type':'application/json'},body:JSON.stringify(body),signal:AbortSignal.timeout(300000)}),'合规审核');
  let text=payload?.choices?.[0]?.message?.content;if(Array.isArray(text))text=text.map(x=>x?.text||'').join('');if(typeof text!=='string'||!text.trim())throw new Error('文本模型没有返回审核结果');return {ok:true,text:text.trim()};
}

function imageFromPayload(payload) {
  const item=payload?.data?.[0] || payload?.result?.data?.[0] || payload?.result?.images?.[0];
  const direct=item?.b64_json || item?.base64;
  if(direct) return {base64:direct};
  const url=item?.url || item?.image_url;
  if(url) return {url};
  const imageUrl=payload?.choices?.[0]?.message?.images?.[0]?.image_url?.url || payload?.choices?.[0]?.message?.images?.[0]?.url;
  if(imageUrl) return imageUrl.startsWith('data:')?{base64:imageUrl.split(',')[1]}:{url:imageUrl};
  const content=payload?.choices?.[0]?.message?.content;
  if(typeof content==='string') {
    const dataMatch=content.match(/data:image\/[\w.+-]+;base64,([A-Za-z0-9+/=]+)/);
    if(dataMatch) return {base64:dataMatch[1]};
    const urlMatch=content.match(/https?:\/\/[^\s)\]]+/);
    if(urlMatch) return {url:urlMatch[0]};
  }
  return null;
}

// 精确编辑 / 图层拆分会一次返回多张图片（底图及若干带 Alpha 的图层）。
// 不把它压成 data[0]，否则界面只能看到第一张。
function imagesFromPayload(payload) {
  const groups=[payload?.data,payload?.result?.data,payload?.result?.images,payload?.images].filter(Array.isArray);
  const result=[];
  for(const group of groups) for(const item of group) {
    if(typeof item==='string'&&/^https?:/i.test(item)) result.push({url:item});
    else if(item) {
      const base64=item.b64_json||item.base64;
      const url=item.url||item.image_url||item.image?.url;
      const meta={name:item.name||item.description||'',bounding_box:item.bounding_box||item.bbox||null,z_index:item.z_index??item.zIndex??null};
      if(base64) result.push({base64,...meta}); else if(url) result.push({url,...meta});
    }
  }
  const unique=[];const seen=new Set();for(const item of result){const key=item.url||item.base64;if(key&&!seen.has(key)){seen.add(key);unique.push(item);}}return unique;
}

const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));

// ToApis 按网络环境提供多个域名：海外为 toapis.com，中国大陆官方文档要求使用 toapis.cn。
// 只认 .com 会让大陆用户被误判为普通 OpenAI 兼容接口，从而把图生图发到 /images/edits，
// 而 sunburst / flare 这类模型只接受 /images/generations，必然失败。
function isToApis(root) {
  try { const host=new URL(root).hostname.toLowerCase();return /(^|\.)toapis\.(com|cn|xyz)$/.test(host); } catch { return false; }
}

function usesToApis(config,root) {
  return String(config?.provider||'').toLowerCase()==='toapis'||isToApis(root);
}

// 火山方舟 Seedream 官方图片 API 使用 /api/v3/images/generations，
// 与 OpenAI 兼容的 ToApis 通道不是同一种请求协议。
function isVolcengineArk(root) {
  try {
    const host=new URL(root).hostname.toLowerCase();
    return host==='ark.cn-beijing.volces.com'||host.endsWith('.volces.com')||host.endsWith('.volcengine.com');
  } catch { return false; }
}

function toApisSupportsQuality(model) {
  const value=String(model||'').toLowerCase();
  return value.includes('official')||value.includes('vip');
}

async function pollImageTask(root,taskId,key) {
  const deadline=Date.now()+600000;let lastStatus='queued';
  await delay(1800);
  while(Date.now()<deadline) {
    const payload=await checked(await fetch(`${root}/images/generations/${encodeURIComponent(taskId)}`,{headers:{Authorization:`Bearer ${key}`},signal:AbortSignal.timeout(120000)}),'查询图像任务');
    const found=imageFromPayload(payload);const status=String(payload?.status||payload?.result?.status||'').toLowerCase();lastStatus=status||lastStatus;
    if(found) return found;
    if(status==='failed'||status==='cancelled'||status==='canceled') {
      const detail=payload?.error?.message||payload?.error||payload?.fail_reason||payload?.message||'服务端未说明原因';
      throw new Error(`图像任务失败：${typeof detail==='string'?detail:JSON.stringify(detail)}`);
    }
    await delay(3000);
  }
  throw new Error(`图像任务等待超时（最后状态：${lastStatus}）`);
}

async function pollImageTaskPayload(root,taskId,key) {
  const deadline=Date.now()+600000;let lastStatus='queued';await delay(1800);
  while(Date.now()<deadline) {
    const payload=await checked(await fetch(`${root}/images/generations/${encodeURIComponent(taskId)}`,{headers:{Authorization:`Bearer ${key}`},signal:AbortSignal.timeout(120000)}),'查询图像任务');
    const images=imagesFromPayload(payload);const status=String(payload?.status||payload?.result?.status||'').toLowerCase();lastStatus=status||lastStatus;
    if(images.length) return payload;
    if(status==='failed'||status==='cancelled'||status==='canceled') {const detail=payload?.error?.message||payload?.error||payload?.fail_reason||payload?.message||'服务端未说明原因';throw new Error(`图像任务失败：${typeof detail==='string'?detail:JSON.stringify(detail)}`);}
    await delay(3000);
  }
  throw new Error(`图像任务等待超时（最后状态：${lastStatus}）`);
}

async function resolveImagePayload(payload,root,key) {
  const found=imageFromPayload(payload);if(found)return found;
  const taskId=payload?.id||payload?.task_id;
  if(taskId&&['generation.task','queued','submitted','in_progress','processing'].includes(String(payload?.object||payload?.status||'').toLowerCase()))return pollImageTask(root,taskId,key);
  if(taskId&&payload?.status&&!['completed','failed'].includes(String(payload.status).toLowerCase()))return pollImageTask(root,taskId,key);
  return null;
}

async function uploadReferenceImage(root,filePath,bytes,key) {
  const form=new FormData();form.append('file',new Blob([bytes],{type:mime(filePath)}),path.basename(filePath));form.append('purpose','generation');
  const payload=await checked(await fetch(root+'/uploads/images',{method:'POST',headers:{Authorization:`Bearer ${key}`},body:form,signal:AbortSignal.timeout(180000)}),'上传参考图片');
  const url=payload?.data?.url||payload?.url;if(!url)throw new Error('图片上传成功，但接口没有返回 data.url');return url;
}

// 图生图支持多张参考图：image_path 是第一张（画布图），reference_images 是附加参考。
async function editReferencePaths(config) {
  const extras=Array.isArray(config.reference_paths)?config.reference_paths:[];
  return [config.image_path,...extras].filter(Boolean);
}

async function toApisEdit(config,root,key,paths,buffers) {
  // ToApis 图生图的公共契约使用单一 image_urls 数组。重复传 reference_images
  // 会让部分上游把同一临时 URL 再次拉取，容易触发下载超时。
  const imageUrls=[];
  for(let i=0;i<paths.length;i++)imageUrls.push(await uploadReferenceImage(root,paths[i],buffers[i],key));
  const shape=proxyImageShape(config);const body={model:config.model,prompt:config.prompt,image_urls:imageUrls,n:1,response_format:'url'};const requestedAspect=toApisImageSize(config,shape);if(requestedAspect)body.size=requestedAspect;if(shape.resolution&&String(shape.resolution).toLowerCase()!=='auto')body.resolution=shape.resolution;
  if(isSeedreamModel(config.model)) { delete body.resolution; applySeedreamNoWatermark(body,shape.resolution); }
  if(toApisSupportsQuality(config.model))body.quality=selectedImageQuality(config);
  if(config.transparent_background){body.background='transparent';body.output_format='png';}
  const payload=await checked(await fetch(root+'/images/generations',{method:'POST',headers:{Authorization:`Bearer ${key}`,'Content-Type':'application/json'},body:JSON.stringify(body),signal:AbortSignal.timeout(180000)}),'创建图像编辑任务');
  const found=await resolveImagePayload(payload,root,key);if(!found)throw new Error('图像编辑任务未返回任务 ID 或图片地址');
  return {ok:true,bytes:await saveImage(found,config.output_path,key),output_path:config.output_path};
}

async function precision(config) {
  const key=process.env.IMAGE_LLM_API_KEY;const root=apiRoot(config.base_url);
  const officialArk=String(config.protocol||'').toLowerCase()==='ark'||isVolcengineArk(root),toApis=usesToApis(config,root);
  if(!officialArk&&!toApis) throw new Error('精确局部编辑请填写火山方舟 Base URL（https://ark.cn-beijing.volces.com/api/v3）；也可继续使用兼容的 ToApis 地址。');
  const bytes=await readFile(config.image_path);
  const requestedSize=String(config.size||'').trim();
  // Seedream 5.0 Pro 官方契约只接受 1K / 2K；其他值安全地落到 2K。
  const size=requestedSize==='1K'||requestedSize==='2K'?requestedSize:'2K';
  const requestedFormat=String(config.output_format||'png').toLowerCase();
  let body;
  if(officialArk) {
    // 官方 API 可直接接受 Base64 参考图，避免先上传临时 URL 后被上游下载超时。
    body={
      model:config.model,
      prompt:config.prompt,
      image:`data:${mime(config.image_path)};base64,${bytes.toString('base64')}`,
      size,
      output_format:requestedFormat==='jpeg'?'jpeg':'png',
      response_format:'url',
      // 方舟官方默认 watermark=true；桌宠明确要求默认不添加水印。
      watermark:false
    };
    // 标准模式采用官方默认；快速模式才显式传入对应优化选项。
    if(String(config.optimize_mode||'').toLowerCase()==='fast') body.optimize_prompt_options={mode:'fast'};
    if(config.transparent_background) { body.background='transparent'; body.output_format='png'; }
    if(config.layer_decomposition) body.layer_decomposition=true;
  } else {
    // 保留已有 ToApis 兼容实现，方便已有用户不迁移配置也能继续使用。
    const imageUrl=await uploadReferenceImage(root,config.image_path,bytes,key);
    body={model:config.model,prompt:config.prompt,image_urls:[imageUrl],size,output_format:requestedFormat==='jpeg'?'jpeg':'png',n:1,response_format:'url'};
    applySeedreamNoWatermark(body,size);
    if(config.transparent_background) { body.background='transparent'; body.output_format='png'; }
    if(config.layer_decomposition) body.layer_decomposition=true;
  }
  let created;
  try {
    created=await checked(await fetch(root+'/images/generations',{method:'POST',headers:{Authorization:`Bearer ${key}`,'Content-Type':'application/json'},body:JSON.stringify(body),signal:AbortSignal.timeout(180000)}),'创建精确编辑任务');
  } catch(error) {
    const message=String(error?.message||error);
    if(config.layer_decomposition&&/could not be processed for layer decomposition/i.test(message)) {
      throw new Error('图像已上传，但 Seedream 当前无法从这张图中识别可拆分图层。请改用主体和背景更清晰的原图；桌宠已自动改用标准无透明 PNG 输入，如仍失败则是模型对该图片内容的限制。');
    }
    throw error;
  }
  let payload=created;let images=imagesFromPayload(payload);const taskId=payload?.id||payload?.task_id;
  if(!images.length&&taskId) {payload=await pollImageTaskPayload(root,taskId,key);images=imagesFromPayload(payload);}
  if(!images.length) throw new Error('精确编辑任务没有返回图片或图层。');
  const outputs=[];const layerInfo=[];for(let index=0;index<images.length;index++) {const item=images[index];const extension=item.base64?'.png':(String(item.url).match(/\.(png|jpe?g|webp)(?:\?|$)/i)?.[1]||'png');const target=path.join(config.output_dir,`precision-${String(index+1).padStart(2,'0')}.${extension==='jpeg'?'jpg':extension}`);await saveImage(item,target,key);outputs.push(target);layerInfo.push({output_path:target,name:item.name||`图层 ${index+1}`,bounding_box:item.bounding_box||null,z_index:item.z_index??index});}
  return {ok:true,output_paths:outputs,layers:layerInfo,layer_count:outputs.length};
}

async function saveImage(found,output,key) {
  let bytes;
  if(found.base64) bytes=Buffer.from(found.base64,'base64');
  else {
    const response=await fetch(found.url,{headers:{Authorization:`Bearer ${key}`},signal:AbortSignal.timeout(180000)});
    if(!response.ok) throw new Error(`下载生成图片失败 HTTP ${response.status}`);
    bytes=Buffer.from(await response.arrayBuffer());
  }
  if(bytes.length<100) throw new Error('图像模型返回的图片内容为空');
  await writeFile(output,bytes); return bytes.length;
}

async function edit(config) {
  const key=process.env.IMAGE_LLM_API_KEY; const root=apiRoot(config.base_url);
  const paths=await editReferencePaths(config);
  const buffers=await Promise.all(paths.map(p=>readFile(p)));
  if(usesToApis(config,root)) return toApisEdit(config,root,key,paths,buffers);
  let firstError='';
  try {
    const form=new FormData(); form.append('model',config.model); form.append('prompt',config.prompt);
    // 单图保持 image 字段以获得最大兼容；多图按 OpenAI 契约使用 image[] 数组。
    if(paths.length>1){for(let i=0;i<paths.length;i++)form.append('image[]',new Blob([buffers[i]],{type:mime(paths[i])}),path.basename(paths[i]));}
    else form.append('image',new Blob([buffers[0]],{type:mime(paths[0])}),path.basename(paths[0]));
    if(isGptImageModel(config.model)){form.append('quality',selectedImageQuality(config));form.append('size',selectedImageSize(config));}
    if(config.transparent_background){form.append('background','transparent');form.append('output_format','png');}
    const payload=await checked(await fetch(root+'/images/edits',{method:'POST',headers:{Authorization:`Bearer ${key}`},body:form,signal:AbortSignal.timeout(600000)}),'图像编辑');
    const found=await resolveImagePayload(payload,root,key); if(!found) throw new Error('图像编辑接口未返回可识别图片字段');
    return {ok:true,bytes:await saveImage(found,config.output_path,key),output_path:config.output_path};
  } catch(error) { firstError=String(error?.message||error); }

  if(config.transparent_background) throw new Error(`透明背景生成失败：${firstError}。已停止回退，避免返回没有 Alpha 通道的假透明图片`);
  // GPT Image 类模型只吃 /images/generations；首选失败后用 chat/completions 回退必然 400，直接报真实原因。
  if(!supportsChatImageFallback(config.model)) throw new Error(`图像编辑失败：${firstError}。该模型的 GPT Image 类接口不支持对话回退`);

  // 回退通道同样携带全部参考图，保证多图语义一致。
  const content=[{type:'text',text:config.prompt}];
  for(let i=0;i<paths.length;i++)content.push({type:'image_url',image_url:{url:`data:${mime(paths[i])};base64,${buffers[i].toString('base64')}`}});
  const body={model:config.model,messages:[{role:'user',content}]};
  const payload=await checked(await fetch(root+'/chat/completions',{method:'POST',headers:{Authorization:`Bearer ${key}`,'Content-Type':'application/json'},body:JSON.stringify(body),signal:AbortSignal.timeout(600000)}),`图像模型回退（首选失败：${firstError}）`);
  const found=imageFromPayload(payload); if(!found) throw new Error(`两种图像调用均未返回图片。首选错误：${firstError}`);
  return {ok:true,bytes:await saveImage(found,config.output_path,key),output_path:config.output_path};
}

async function generate(config) {
  const key=process.env.IMAGE_LLM_API_KEY;const root=apiRoot(config.base_url);let firstError='';
  try {
    const toApis=usesToApis(config,root),gptImage=isGptImageModel(config.model),requestedSize=selectedImageSize(config),shape=proxyImageShape(config);const sizeValue=toApis?toApisImageSize(config,shape):(gptImage?requestedSize:(requestedSize==='auto'?'1024x1024':requestedSize));const body={model:config.model,prompt:config.prompt,n:1};if(sizeValue)body.size=sizeValue;if(toApis){body.response_format='url';if(shape.resolution&&String(shape.resolution).toLowerCase()!=='auto')body.resolution=shape.resolution;if(isSeedreamModel(config.model)){delete body.resolution;applySeedreamNoWatermark(body,shape.resolution);}if(toApisSupportsQuality(config.model))body.quality=selectedImageQuality(config);}else if(gptImage)body.quality=selectedImageQuality(config);else body.response_format='b64_json';if(config.transparent_background){body.background='transparent';body.output_format='png';}
    let payload;
    try {payload=await checked(await fetch(root+'/images/generations',{method:'POST',headers:{Authorization:`Bearer ${key}`,'Content-Type':'application/json'},body:JSON.stringify(body),signal:AbortSignal.timeout(600000)}),'文生图');}
    catch(error) {
      // Many compatible gateways reject optional OpenAI fields such as size,
      // n, or response_format. Retry the same Images endpoint with only the
      // portable model/prompt pair before abandoning that API family.
      if(config.transparent_background) throw error;
      const minimal={model:config.model,prompt:config.prompt};
      payload=await checked(await fetch(root+'/images/generations',{method:'POST',headers:{Authorization:`Bearer ${key}`,'Content-Type':'application/json'},body:JSON.stringify(minimal),signal:AbortSignal.timeout(600000)}),'文生图（兼容重试）');
    }
    const found=await resolveImagePayload(payload,root,key);if(!found)throw new Error('文生图接口未返回可识别图片字段');
    return {ok:true,bytes:await saveImage(found,config.output_path,key),output_path:config.output_path};
  } catch(error) { firstError=String(error?.message||error); }
  if(config.transparent_background) throw new Error(`透明背景生成失败：${firstError}。请使用支持透明背景的 GPT Image 模型；已停止回退，避免返回白底图片`);
  // GPT Image 类模型只吃 /images/generations；首选失败后用 chat/completions 回退必然 400，直接报真实原因。
  if(!supportsChatImageFallback(config.model)) throw new Error(`文生图失败：${firstError}。该模型的 GPT Image 类接口不支持对话回退`);
  const body={model:config.model,messages:[{role:'user',content:`请生成一张图片并直接返回图片。要求：${config.prompt}`} ]};
  const payload=await checked(await fetch(root+'/chat/completions',{method:'POST',headers:{Authorization:`Bearer ${key}`,'Content-Type':'application/json'},body:JSON.stringify(body),signal:AbortSignal.timeout(600000)}),`图像模型回退（首选失败：${firstError}）`);
  const found=imageFromPayload(payload);if(!found)throw new Error(`两种文生图调用均未返回图片。首选错误：${firstError}`);
  return {ok:true,bytes:await saveImage(found,config.output_path,key),output_path:config.output_path};
}

async function main(){
  try{
    const config=JSON.parse(await readFile(process.argv[2],'utf8')); let result;
    if(config.mode==='models') result=await models(config,config.key_type==='text'?process.env.TEXT_LLM_API_KEY:process.env.IMAGE_LLM_API_KEY);
    else if(config.mode==='ocr') result=await ocr(config);
    else if(config.mode==='ocr_layout') result=await ocrLayout(config);
    else if(config.mode==='chat') result=await chat(config);
    else if(config.mode==='layer_layout') result=await layerLayout(config);
    else if(config.mode==='audit') result=await audit(config);
    else if(config.mode==='precision') result=await precision(config);
    else if(config.mode==='edit') result=await edit(config);
    else if(config.mode==='generate') result=await generate(config);
    else throw new Error('未知图像 AI 操作');
    process.stdout.write(JSON.stringify(result));
  }catch(error){
    const cause=error?.cause;const parts=[error?.message,cause?.code,cause?.message].filter(Boolean);let message=[...new Set(parts.map(String))].join(' · ');
    if(String(error?.message||'').toLowerCase().includes('fetch failed'))message=`连接不到当前 Base URL${message?`（${message}）`:''}。请核对地址是否包含正确的 /v1；若浏览器能打开但这里仍失败，请检查该域名是否只能通过代理访问`;
    process.stdout.write(JSON.stringify({ok:false,error:message||String(error)}));process.exitCode=1
  }
}
await main();
