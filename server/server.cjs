const http = require('node:http');
const https = require('node:https');
const { randomBytes, timingSafeEqual } = require('node:crypto');
const { mkdirSync, readFileSync, writeFileSync } = require('node:fs');
const { readFile } = require('node:fs/promises');
const { execFile } = require('node:child_process');
const { BlockList } = require('node:net');
const { networkInterfaces } = require('node:os');
const { promisify } = require('node:util');
const { join } = require('node:path');

const run = promisify(execFile);
const containerHosts = process.env.COZE_CONTAINER_HOSTS?.split(',');
const publicOrigin = process.env.COZE_PUBLIC_ORIGIN ? new URL(process.env.COZE_PUBLIC_ORIGIN) : null;
if (publicOrigin && (publicOrigin.protocol !== 'https:' || publicOrigin.href !== publicOrigin.origin + '/')) {
  throw new Error('COZE_PUBLIC_ORIGIN must be an HTTPS origin without a path.');
}
const port = containerHosts ? 8766 : 8765;
const botId = '7601089030063620131';
const localData = join(__dirname, '..', '.local');
const tokenFile = join(localData, 'coze-token.dpapi');
const accessFile = containerHosts
  ? '/run/secrets/coze_access_code'
  : join(localData, 'coze-lan-access.txt');
if (!containerHosts) {
  mkdirSync(localData, { recursive: true });
  try {
    writeFileSync(accessFile, randomBytes(24).toString('hex'), { flag: 'wx' });
  } catch (error) {
    if (error.code !== 'EEXIST') throw error;
  }
}
const accessKey = Buffer.from(readFileSync(accessFile, 'utf8').trim());
if (!accessKey.length) throw new Error('The chat access code is empty.');
const assets = {
  '/': ['index.html', 'text/html; charset=utf-8'],
  '/app.js': ['app.js', 'text/javascript; charset=utf-8'],
};
class ChatError extends Error {}

function onSameSubnet(localAddress, remoteAddress) {
  const addresses = Object.values(networkInterfaces()).flat();
  const local = addresses.find(address => address.family === 'IPv4' && address.address === localAddress);
  if (!local?.cidr) return false;
  const subnet = new BlockList();
  subnet.addSubnet(local.address, Number(local.cidr.split('/')[1]), 'ipv4');
  return subnet.check(remoteAddress, 'ipv4');
}

function json(res, status, body) {
  res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8' });
  res.end(JSON.stringify(body));
}

async function readToken() {
  try {
    if (containerHosts) {
      const token = (await readFile('/run/secrets/coze_api_token', 'utf8')).trim();
      if (token) return token;
      throw new ChatError('请配置扣子访问令牌，然后重新运行 Docker 启动脚本。');
    }
    const { stdout } = await run('powershell.exe', [
      '-NoProfile', '-NonInteractive', '-Command',
      '$ErrorActionPreference = "Stop"; ' +
      '$token = (Get-Content -LiteralPath $env:COZE_LOCAL_TOKEN_FILE -Raw).Trim() | ConvertTo-SecureString; ' +
      '[Console]::Write([System.Net.NetworkCredential]::new("", $token).Password)',
    ], { windowsHide: true, timeout: 8000, env: { ...process.env, COZE_LOCAL_TOKEN_FILE: tokenFile } });
    if (stdout.trim()) return stdout.trim();
  } catch {
    // A missing secret or an unreadable Windows credential needs configuration.
  }
  if (containerHosts) {
    throw new ChatError('请配置扣子访问令牌，然后重新运行 Start-Docker.cmd。');
  }
  throw new ChatError('请先运行 Configure-Coze.cmd，在本机配置扣子访问令牌。');
}

function cozeError(code) {
  if (Number(code) === 4101) {
    const restart = containerHosts ? '，然后运行 Start-Docker.cmd' : '';
    return new ChatError('扣子拒绝了访问令牌（4101）。请运行 Configure-Coze.cmd 更换令牌' + restart + '。');
  }
  if (Number(code) === 4015) return new ChatError('扣子提示智能体尚未发布为 API 服务（4015）。');
  return new ChatError('扣子对话失败' + (Number(code) ? '（错误码 ' + Number(code) + '）' : '') + '。');
}

async function chat(input) {
  const url = new URL('https://api.coze.cn/v3/chat');
  if (input.conversationId) url.searchParams.set('conversation_id', input.conversationId);
  const token = await readToken();
  const body = JSON.stringify({
      bot_id: botId,
      user_id: input.userId,
      stream: true,
      auto_save_history: true,
      additional_messages: [{ role: 'user', content: input.message, content_type: 'text' }],
  });
  const upstream = await new Promise((resolve, reject) => {
    const request = https.request(url, {
      method: 'POST',
      // The Docker bridge has an IPv4 route, but its resolver can return only IPv6
      // addresses when the lookup does not explicitly request an address family.
      family: containerHosts ? 4 : undefined,
      signal: AbortSignal.timeout(120_000),
      headers: {
        Authorization: 'Bearer ' + token,
        'Content-Type': 'application/json',
        'Content-Length': Buffer.byteLength(body),
      },
    }, resolve);
    request.on('error', reject);
    request.end(body);
  });
  if (!upstream.headers['content-type']?.includes('text/event-stream')) {
    const chunks = [];
    for await (const chunk of upstream) chunks.push(chunk);
    let data;
    try { data = JSON.parse(Buffer.concat(chunks).toString('utf8')); } catch { /* non-JSON error */ }
    throw cozeError(data?.code || upstream.statusCode);
  }
  if (upstream.statusCode < 200 || upstream.statusCode >= 300) {
    upstream.resume();
    throw cozeError(upstream.statusCode);
  }

  const answers = [];
  let conversationId = input.conversationId;
  let completed = false;
  let event = '';
  let payload = [];
  const lines = require('node:readline').createInterface({
    input: upstream,
    crlfDelay: Infinity,
  });
  for await (const line of lines) {
    if (line.startsWith('event:')) event = line.slice(6).trim();
    if (line.startsWith('data:')) payload.push(line.slice(5).replace(/^ /, ''));
    if (line !== '') continue;

    const data = payload.length && event !== 'done' ? JSON.parse(payload.join('\n')) : {};
    if (event === 'error' || event === 'conversation.chat.failed') {
      throw cozeError(data.code || data.last_error?.code);
    }
    if (event === 'conversation.chat.requires_action') {
      throw new ChatError('该对话需要提交客户端插件的执行结果。');
    }
    if (event === 'conversation.chat.created') conversationId = data.conversation_id;
    if (event === 'conversation.message.completed' && data.type === 'answer' && typeof data.content === 'string') {
      answers.push(data.content);
    }
    if (event === 'conversation.chat.completed') {
      conversationId = data.conversation_id || conversationId;
      completed = true;
    }
    if (event === 'done') break;
    event = '';
    payload = [];
  }
  if (!completed) throw new ChatError('扣子连接在对话完成前中断，请重试。');
  if (!answers.some(answer => answer.trim())) throw new ChatError('扣子已完成对话，但没有返回文本回复。');
  return { answer: answers.join('\n\n'), conversationId };
}

const server = http.createServer(async (req, res) => {
  res.setHeader('Cache-Control', 'no-store');
  res.setHeader('X-Content-Type-Options', 'nosniff');
  res.setHeader('Content-Security-Policy', "default-src 'self'; style-src 'unsafe-inline'; frame-ancestors 'none'");
  const { localAddress, remoteAddress } = req.socket;
  const localRequest = !containerHosts && remoteAddress === '127.0.0.1';
  const host = req.headers.host;
  const publicRequest = publicOrigin?.host === host && Boolean(containerHosts);
  const allowedHost = containerHosts
    ? publicRequest || containerHosts.includes(host)
    : host === localAddress + ':' + port && (localRequest || onSameSubnet(localAddress, remoteAddress));
  if (!allowedHost) {
    return json(res, 403, { error: '请使用本机或同一局域网的访问地址。' });
  }
  if (req.method === 'GET' && req.url === '/health') return json(res, 200, { service: 'coze-local-agent' });
  const asset = req.method === 'GET' && Object.hasOwn(assets, req.url) && assets[req.url];
  if (asset) {
    try {
      const content = await readFile(join(__dirname, asset[0]));
      res.writeHead(200, { 'Content-Type': asset[1] });
      return res.end(content);
    } catch {
      return json(res, 500, { error: '无法读取页面文件。' });
    }
  }
  if (req.method !== 'POST' || req.url !== '/api/chat') return json(res, 404, { error: '页面不存在。' });
  const expectedOrigin = publicRequest ? publicOrigin.origin : 'http://' + host;
  if (req.headers.origin !== expectedOrigin || !/^application\/json(?:;|$)/i.test(req.headers['content-type'] || '')) {
    return json(res, 403, { error: '请从聊天页面发送消息。' });
  }
  if (!localRequest) {
    const provided = Buffer.from(req.headers['x-coze-access'] || '');
    if (provided.length !== accessKey.length || !timingSafeEqual(provided, accessKey)) {
      return json(res, 403, { error: '请使用带访问码的完整局域网链接打开聊天页面。' });
    }
  }
  let input;
  try {
    const chunks = [];
    let size = 0;
    for await (const chunk of req) {
      size += chunk.length;
      if (size > 32768) return json(res, 413, { error: '消息太长。' });
      chunks.push(chunk);
    }
    input = JSON.parse(Buffer.concat(chunks).toString('utf8'));
  } catch {
    return json(res, 400, { error: '消息格式无效。' });
  }
  if (typeof input?.message !== 'string' || !input.message.trim() || input.message.length > 4000 ||
      typeof input.userId !== 'string' || !/^(?:[a-f0-9-]{36}|[a-f0-9]{32})$/.test(input.userId) ||
      (input.conversationId != null && (typeof input.conversationId !== 'string' || !/^\d{1,32}$/.test(input.conversationId)))) {
    return json(res, 400, { error: '请检查消息长度或重新打开聊天页面。' });
  }
  try {
    return json(res, 200, await chat(input));
  } catch (error) {
    const message = error.name === 'TimeoutError' || error.name === 'AbortError'
      ? '扣子响应超时，请重试。'
      : error instanceof ChatError
        ? error.message
        : '无法完成扣子请求，请检查本机网络后重试。';
    return json(res, 502, { error: message });
  }
});

server.listen(port, '0.0.0.0', () => console.log('Coze chat is listening on port ' + port));
