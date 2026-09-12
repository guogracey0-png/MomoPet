import { readFile, writeFile, unlink } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import path from 'node:path';

const root = process.cwd();
const windSkill = path.join(root, '.agents', 'skills', 'wind-mcp-skill');
const windCli = path.join(windSkill, 'scripts', 'cli.mjs');
const aliceCli = path.join(root, '.agents', 'skills', 'wind-alice', 'scripts', 'wind-alice.mjs');

function endpoint(baseUrl, suffix) {
  let base = String(baseUrl || '').trim().replace(/\/+$/, '');
  if (!base) throw new Error('请先设置大模型 Base URL');
  if (base.endsWith('/chat/completions')) base = base.slice(0, -'/chat/completions'.length);
  if (base.endsWith('/models')) base = base.slice(0, -'/models'.length);
  return base + suffix;
}

async function requestJson(url, apiKey, body) {
  const response = await fetch(url, {
    method: body ? 'POST' : 'GET',
    headers: {
      Authorization: `Bearer ${apiKey}`,
      Accept: 'application/json',
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
    signal: AbortSignal.timeout(120000),
  });
  const text = await response.text();
  if (!response.ok) throw new Error(`大模型服务返回 HTTP ${response.status}: ${text.slice(0, 500)}`);
  try { return JSON.parse(text); }
  catch { throw new Error(`大模型服务未返回 JSON: ${text.slice(0, 500)}`); }
}

async function chat(config, system, user) {
  const payload = await requestJson(endpoint(config.base_url, '/chat/completions'), process.env.LLM_API_KEY, {
    model: config.model,
    messages: [{ role: 'system', content: system }, { role: 'user', content: user }],
  });
  const content = payload?.choices?.[0]?.message?.content;
  if (typeof content !== 'string' || !content.trim()) throw new Error('大模型没有返回有效内容');
  return content.trim();
}

function jsonFromModel(text) {
  const cleaned = text.replace(/^```(?:json)?\s*/i, '').replace(/\s*```$/, '').trim();
  try { return JSON.parse(cleaned); }
  catch {
    const start = cleaned.indexOf('{');
    const end = cleaned.lastIndexOf('}');
    if (start >= 0 && end > start) return JSON.parse(cleaned.slice(start, end + 1));
    throw new Error('大模型路由结果不是有效 JSON');
  }
}

function toolList(tools) {
  if (Array.isArray(tools)) return tools;
  if (Array.isArray(tools?.tools)) return tools.tools;
  if (Array.isArray(tools?.result?.tools)) return tools.result.tools;
  return [];
}

function compactToolsJson(tools) {
  const compact = toolList(tools).map(tool => ({
    name: tool?.name,
    description: String(tool?.description || '').slice(0, 1600),
    inputSchema: tool?.inputSchema || tool?.input_schema || tool?.parameters || {},
  })).filter(tool => tool.name);
  return JSON.stringify(compact);
}

function parseArguments(value) {
  if (value && typeof value === 'object' && !Array.isArray(value)) return value;
  if (typeof value !== 'string' || !value.trim()) return null;
  try { const parsed = JSON.parse(value); return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed : null; }
  catch { return null; }
}

function normalizeCalls(route) {
  const raw = Array.isArray(route?.calls) ? route.calls
    : Array.isArray(route?.tool_calls) ? route.tool_calls
    : route?.tool_name || route?.name || route?.function?.name ? [route] : [];
  return raw.map(item => {
    const fn = item?.function && typeof item.function === 'object' ? item.function : item;
    const toolName = fn?.tool_name || fn?.name || item?.tool_name || item?.name;
    const args = parseArguments(fn?.arguments ?? fn?.args ?? fn?.parameters ?? item?.arguments ?? item?.args ?? item?.parameters);
    return toolName && args ? { tool_name: toolName, arguments: args, purpose: String(item?.purpose || fn?.purpose || '') } : null;
  }).filter(Boolean);
}

function needsAnalytics(question) {
  const text = String(question || '').replace(/\s+/g, '');
  return /(行业|板块).*(占比|权重|分布|构成)|(?:占比|权重|分布|构成).*(行业|板块)|前[一二三四五六七八九十百\d]+大?(行业|板块)|成[份分].*(行业|板块).*(聚合|统计|占比|权重)/.test(text);
}

function wantsIndustryWeight(question) {
  const text = String(question || '').replace(/\s+/g, '');
  if (/(数量占比|家数占比|只数占比|成[份分]股数量占比)/.test(text)) return false;
  return /(行业|板块).*(占比|权重|分布|构成)|(?:占比|权重|分布|构成).*(行业|板块)|前[一二三四五六七八九十百\d]+大?(行业|板块)/.test(text);
}

function analyticsQuestion(question) {
  if (!wantsIndustryWeight(question)) return question;
  return `${question}\n严格口径要求：这里的“行业占比/构成”是指数成份股权重按指定行业分类汇总后的指数权重占比，不是各行业成份股数量、家数或只数占全部成份股数量的比例。按查询日期有效的指数成份与权重计算；列出前十大行业，并将其余全部行业的权重合并为“其他”，全部占比合计应约为100%。结果必须包含“行业权重占比”，成份股数量只能作为附加信息，绝不能据此计算占比。`;
}

function fallbackCalls(serverType, question, tools) {
  const names = new Set(toolList(tools).map(tool => tool?.name).filter(Boolean));
  if (serverType === 'analytics_data' && names.has('get_financial_data')) {
    return [{ tool_name: 'get_financial_data', arguments: { question: analyticsQuestion(question) }, purpose: wantsIndustryWeight(question) ? '按指数成份权重汇总行业占比（禁止按成份数量计算）' : '按用户原始口径完成跨成份聚合与结构化取数' }];
  }
  return [];
}

function knownCalls(calls, tools) {
  const names = new Set(toolList(tools).map(tool => tool?.name).filter(Boolean));
  return calls.filter(call => names.has(call.tool_name));
}

function runNode(args, cwd, extraEnv = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn('node', args, {
      cwd,
      windowsHide: true,
      env: { ...process.env, ...extraEnv },
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    let stdout = '', stderr = '';
    child.stdout.setEncoding('utf8'); child.stderr.setEncoding('utf8');
    child.stdout.on('data', chunk => { stdout += chunk; });
    child.stderr.on('data', chunk => { stderr += chunk; });
    child.on('error', reject);
    child.on('close', code => resolve({ code, stdout, stderr }));
  });
}

function requireSuccess(result, label) {
  if (result.code === 0) return result.stdout;
  let message = result.stderr.trim() || result.stdout.trim();
  try { message = JSON.parse(result.stdout).message || message; } catch { }
  throw new Error(`${label}失败：${message.slice(0, 1200)}`);
}

function windText(stdout) {
  try {
    const outer = JSON.parse(stdout);
    const text = outer?.content?.find?.(item => item?.type === 'text')?.text;
    return typeof text === 'string' ? text : JSON.stringify(outer);
  } catch { return stdout; }
}

async function listModels(config) {
  const payload = await requestJson(endpoint(config.base_url, '/models'), process.env.LLM_API_KEY);
  const source = Array.isArray(payload?.data) ? payload.data : Array.isArray(payload?.models) ? payload.models : [];
  const models = source.map(item => typeof item === 'string' ? item : item?.id || item?.name || item?.model).filter(Boolean);
  if (!models.length) throw new Error(`模型接口返回成功，但没有发现 data[].id 或 models[]。返回字段：${Object.keys(payload||{}).join(', ')}`);
  return { ok: true, models: [...new Set(models)].sort() };
}

async function search(config) {
  const question = String(config.question || '').trim();
  if (!question) throw new Error('请输入要搜索的问题');
  if (!config.model) throw new Error('请先选择大模型');

  const classifySystem = `你是 Wind 金融数据路由器。只返回 JSON：{"server_type":"..."}。
可选 server_type：
stock_data 股票筛选、行情、财务、股东、事件、技术、风险；
fund_data 基金/ETF/LOF；index_data 指数与板块的标准行情、K线、档案和预定义基本面；bond_data 债券；
financial_docs 公告、年报、招股书、财经新闻；economic_data 宏观、行业、汇率；
analytics_data 跨标的聚合、排名、复合指标，以及指数成份权重、行业分布/构成/占比、前N行业等需要对成份数据二次聚合的问题；
wind_alice 仅用于需要综合研究、解释、投资备忘、事实核验或专业报告的问题。
问题即使提到指数，只要要求按行业或板块计算权重、占比、分布、构成或前N排名，也必须选 analytics_data，不得选 index_data。
简单事实取数不得选 wind_alice。不要回答问题本身。`;
  const classified = jsonFromModel(await chat(config, classifySystem, question));
  let serverType = classified.server_type;
  // 模型经常看到“指数”就误选 index_data；行业构成属于确定性的跨成份聚合，直接纠偏。
  if (needsAnalytics(question)) serverType = 'analytics_data';
  const allowed = new Set(['stock_data','fund_data','index_data','bond_data','financial_docs','economic_data','analytics_data','wind_alice']);
  if (!allowed.has(serverType)) throw new Error(`大模型返回了不支持的数据域：${serverType}`);

  if (serverType === 'wind_alice') {
    const alice = requireSuccess(await runNode([aliceCli, '--prompt', question], path.dirname(aliceCli), {
      WIND_API_KEY: process.env.WIND_API_KEY,
    }), 'Wind Alice');
    return { ok: true, route: { server_type: serverType }, result: alice.trim(), source: 'Wind Alice' };
  }

  const toolsRaw = requireSuccess(await runNode([windCli, 'list-tools', serverType], windSkill, {
    WIND_API_KEY: process.env.WIND_API_KEY,
  }), '读取 Wind MCP 工具');
  const tools = JSON.parse(toolsRaw);
  const toolsSchema = compactToolsJson(tools);
  const routeSystem = `你是 Wind MCP 工具调用器。根据用户原问题和后端实时 tools schema，拆分出覆盖全部问题所需的工具调用；调用会按顺序串行执行。不要人为限制调用数量，直到每个可查询子问题都有对应调用。
只返回 JSON：{"calls":[{"tool_name":"精确工具名","arguments":{...},"purpose":"本调用回答的子问题"}]}。
一个问题同时要求“区间涨跌幅、指数权重、成分、估值、行情”等不同口径时，必须分别选择能回答每个口径的工具；禁止用静态基本信息替代区间行情或成分权重。
若 schema 中确实没有可取某项数据的工具，不要伪造调用；仍为其他可查询部分创建调用，并在 purpose 中写明“该项无对应工具”。
参数名、类型、必填项必须逐字符合 schema；不要回答问题，不要添加 schema 之外字段。
对于日期必须用明确的 yyyy-MM-dd；用户未给日期且 schema 允许省略时不要臆造。
tools schema：${toolsSchema}`;
  // 这类行业构成问题的工具选择是确定的，直接构造调用，避免白白消耗一次模型路由并减少格式错误。
  let calls = needsAnalytics(question) ? fallbackCalls(serverType, question, tools) : [];
  let routeText = '', route = null;
  if (!calls.length) {
    routeText = await chat(config, routeSystem, question);
    route = jsonFromModel(routeText);
    // 兼容 calls、tool_calls、单工具对象、OpenAI function 包装，以及字符串形式的 arguments。
    calls = knownCalls(normalizeCalls(route), tools);
  }
  if (!calls.length) {
    const repairSystem = `你上一次没有返回可执行的 Wind 工具调用。请严格修复为 JSON：{"calls":[{"tool_name":"精确工具名","arguments":{},"purpose":"用途"}]}。arguments 必须是 JSON 对象，工具名和必填参数必须符合 schema；不要解释。tools schema：${toolsSchema}`;
    try { routeText = await chat(config, repairSystem, `用户原问题：${question}\n\n上一次输出：${routeText}`); route = jsonFromModel(routeText); calls = knownCalls(normalizeCalls(route), tools); } catch { calls = []; }
  }
  if (!calls.length) calls = fallbackCalls(serverType, question, tools);
  if (!calls.length) throw new Error('大模型未生成可执行的 Wind 工具调用，自动修复后仍不完整');

  const responses=[];
  for(let index=0;index<calls.length;index++) {
    const call=calls[index];
    const requestName = `request-momopet-ai-${process.pid}-${Date.now()}-${index}.json`;
    const requestPath = path.join(windSkill, 'scripts', requestName);
    try {
      await writeFile(requestPath, JSON.stringify(call.arguments), 'utf8');
      const windRaw = requireSuccess(await runNode([
        windCli, 'call', serverType, call.tool_name, `@scripts/${requestName}`,
      ], windSkill, { WIND_API_KEY: process.env.WIND_API_KEY }), `Wind MCP 查询（${call.purpose||call.tool_name}）`);
      responses.push({ call, data: windText(windRaw).slice(0, 120000) });
    } catch(error) {
      // 已成功的子查询仍可交给整理器呈现；失败项会明确写出，绝不拿其他字段冒充答案。
      responses.push({ call, error: String(error?.message||error) });
    } finally {
      await unlink(requestPath).catch(() => {});
    }
  }
  if(!responses.some(item=>item.data)) throw new Error(responses.map(item=>item.error).filter(Boolean).join('\n')||'Wind MCP 没有返回可用数据');
  const data=responses.map((item,index)=>item.data?`【子查询 ${index+1}：${item.call.purpose||item.call.tool_name}】\n${item.data}`:`【子查询 ${index+1} 未完成：${item.call.purpose||item.call.tool_name}】\n${item.error}`).join('\n\n');
  const answerSystem = `你是金融数据结果整理器。只根据提供的 Wind 返回数据回答用户原问题，不得补充外部事实、常识或猜测。
保留返回中的单位、日期、缺失值和警告；null 表示缺失，不得当作 0。结果要清楚、便于复制。
凡是包含多个标的、多个日期或多个可比较字段的数据，优先整理成标准 Markdown 表格；表头简洁，同一列保持相同单位，数值不要混入解释文字。只有不适合二维结构的信息才使用普通段落或列表。
当用户要求指数的行业占比、行业权重、行业构成或行业分布时，“占比”默认且只能指指数成份权重按行业汇总后的权重占比；不得用行业成份股数量除以总成份股数量冒充权重占比。除非用户明确写了“数量占比/家数占比/只数占比”。如果 Wind 只返回成份股数量而没有权重字段，必须明确写“当前查询未取得行业权重占比”，禁止自行用数量计算百分比。
对每一项原问题逐项作答：如果某项子查询失败或当前 Wind 工具没有对应字段，必须明确写“当前查询未能取得该项”，并说明原因；绝不能把其他字段、代码或静态信息当成该项答案。
最后单独附一行：数据来源于万得 Wind 金融数据服务。`;
  const result = await chat(config, answerSystem, `用户原问题：${question}\n\nWind 返回：${data}`);
  return { ok: true, route: { server_type: serverType, calls:calls.map(call=>({tool_name:call.tool_name,purpose:call.purpose||''})) }, result, source: 'Wind MCP' };
}

async function main() {
  try {
    const config = JSON.parse(await readFile(process.argv[2], 'utf8'));
    if (!process.env.LLM_API_KEY) throw new Error('请先保存大模型 API Key');
    const output = config.mode === 'models' ? await listModels(config) : await search(config);
    process.stdout.write(JSON.stringify(output));
  } catch (error) {
    process.stdout.write(JSON.stringify({ ok: false, error: String(error?.message || error) }));
    process.exitCode = 1;
  }
}

await main();
