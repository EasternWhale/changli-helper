const form = document.querySelector('form');
const input = document.querySelector('#message');
const messages = document.querySelector('#messages');
const status = document.querySelector('#status');
const send = document.querySelector('#send');
const reset = document.querySelector('#reset');
const promptButtons = [...document.querySelectorAll('[data-prompt]')];
const workflows = document.querySelector('#workflows');
const welcome = workflows.querySelector('.welcome');
const nav = workflows.querySelector('nav');
const task = document.querySelector('#task');
const product = document.querySelector('#product');
const goal = document.querySelector('#goal');
const goalLabel = document.querySelector('#goal-label');
const tasks = [
  ['助手能力', '了解可用功能与示例', '想了解的方向', '例如：非遗文创推广', '介绍你能帮我完成的任务，并给出可直接开始的例子。'],
  ['短视频文案', '从开场、画面到结尾', '平台或风格', '例如：小红书，温暖自然', '为这个产品写一段短视频文案，给出开场、画面建议和结尾。'],
  ['非遗产品推荐', '按场景、用途和预算选品', '使用场景或预算', '例如：酒店大堂，预算 3000 元', '按使用场景推荐合适的非遗产品，并说明搭配理由。'],
  ['目标客群分析', '了解购买动机与销售渠道', '价格或销售渠道', '例如：定价 129 元，线上销售', '分析产品的目标客群、购买动机和适合的销售渠道。'],
  ['品牌故事', '梳理工艺特色与店铺叙事', '品牌特色或语气', '例如：苏绣手作，朴实温暖', '写一段可用于店铺首页的品牌故事，突出工艺和特色。'],
];
let selectedTask;
const taskButtons = tasks.map(([name, description], index) => {
  const button = document.createElement('button');
  button.type = 'button';
  button.setAttribute('aria-controls', 'task');
  const title = document.createElement('strong');
  title.textContent = name;
  const detail = document.createElement('small');
  detail.textContent = description;
  button.append(title, detail);
  button.addEventListener('click', () => selectTask(selectedTask === index ? undefined : index));
  nav.append(button);
  return button;
});

function selectTask(index) {
  selectedTask = index;
  task.hidden = index === undefined;
  taskButtons.forEach((button, i) => button.setAttribute('aria-expanded', String(i === index)));
  if (index === undefined) return;
  goalLabel.textContent = tasks[index][2];
  goal.placeholder = tasks[index][3];
  goal.value = '';
  product.focus();
}
document.querySelector('#close-task').addEventListener('click', () => {
  const button = taskButtons[selectedTask];
  selectTask();
  button?.focus();
});
document.querySelector('#fill-task').addEventListener('click', () => {
  if (selectedTask === undefined) return;
  const [, , label, , instruction] = tasks[selectedTask];
  const draft = [instruction];
  if (product.value.trim()) draft.push('产品、品牌或品类：' + product.value.trim());
  if (goal.value.trim()) draft.push(label + '：' + goal.value.trim());
  if (input.value.trim()) draft.push('补充说明：' + input.value.trim());
  const message = draft.join('\n');
  if (message.length > input.maxLength) {
    status.textContent = '请先缩短输入框中的草稿。';
    return;
  }
  input.value = message;
  selectTask();
  input.focus();
});
const accessKey = new URLSearchParams(location.hash.slice(1)).get('access');
if (accessKey) {
  sessionStorage.setItem('cozeLanAccess', accessKey);
  history.replaceState(null, '', location.pathname + location.search);
}
const headers = { 'Content-Type': 'application/json' };
const storedAccessKey = sessionStorage.getItem('cozeLanAccess');
if (storedAccessKey) headers['X-Coze-Access'] = storedAccessKey;
const userId = localStorage.getItem('cozeUserId') ||
  Array.from(crypto.getRandomValues(new Uint8Array(16)), byte => byte.toString(16).padStart(2, '0')).join('');
localStorage.setItem('cozeUserId', userId);
let conversationId;

function addMessage(label, message, kind) {
  welcome.hidden = true;
  workflows.classList.add('active');
  const entry = document.createElement('article');
  entry.className = 'entry ' + kind;
  const title = document.createElement('strong');
  title.textContent = label;
  const content = document.createElement('p');
  content.textContent = message;
  entry.append(title, content);
  messages.append(entry);
  entry.scrollIntoView({ block: 'end' });
}

function newConversation() {
  conversationId = undefined;
  messages.replaceChildren();
  welcome.hidden = false;
  workflows.classList.remove('active');
  product.value = '';
  selectTask();
  status.textContent = '';
  input.focus();
}

reset.addEventListener('click', newConversation);
form.addEventListener('submit', async event => {
  event.preventDefault();
  await submit(input.value.trim(), true);
});
for (const button of promptButtons) {
  button.addEventListener('click', () => submit(button.dataset.prompt, false));
}
async function submit(message, fromInput) {
  if (!message || send.disabled) return;
  selectTask();
  addMessage('你', message, 'user');
  if (fromInput) input.value = '';
  send.disabled = reset.disabled = true;
  [...promptButtons, ...taskButtons].forEach(button => { button.disabled = true; });
  status.textContent = '正在等待长离助手回复…';
  try {
    const response = await fetch('/api/chat', {
      method: 'POST',
      headers,
      body: JSON.stringify({ message, userId, conversationId }),
    });
    const data = await response.json();
    if (!response.ok) throw new Error(data.error);
    conversationId = data.conversationId;
    addMessage('长离助手', data.answer, 'agent');
  } catch (error) {
    addMessage('请求失败', error.message, 'error');
    if (!input.value) input.value = message;
  } finally {
    send.disabled = reset.disabled = false;
    [...promptButtons, ...taskButtons].forEach(button => { button.disabled = false; });
    status.textContent = '';
    input.focus();
  }
}
newConversation();
