// BABEL: o script que roda dentro de cada página da Steam.
// Acha o texto estrangeiro que está na tela, pede a tradução ao serviço (pela binding __babelSend)
// e troca o texto NO LUGAR: só o conteúdo dos nós de texto muda, a estrutura da página não.
// Isso importa porque a biblioteca e o chat são React; trocar elementos quebraria a interface.
(() => {
  'use strict';
  // ---------- 0. Onde este script pode trabalhar ----------
  // Só no quadro principal e só nos endereços que o serviço permite (a mesma regra do Pages.Judge, passada pelo
  // serviço). Uma navegação para conta, carteira ou pagamento cai fora ANTES de ler qualquer texto.
  const PERMITIDO = __PERMITIDO__;
  const permitido = (loc) => {
    if (loc.protocol === 'about:') return PERMITIDO.cliente;
    if (loc.protocol !== 'https:' || !PERMITIDO.hosts.includes(loc.hostname.toLowerCase())) return false;
    const p = loc.pathname.toLowerCase();
    return !PERMITIDO.sensiveis.some((s) => p.startsWith(s) || p.includes(s + '/') || p.endsWith(s));
  };
  if (window !== window.top) return;
  if (!permitido(location)) {
    if (window.__babel && window.__babel.parar) window.__babel.parar();
    return;
  }
  // ---------- fim da parte 0 ----------
  const VERSAO = '__VERSAO__';
  if (typeof window.__babelSend !== 'function' || (window.__babel && window.__babel.versao === VERSAO)) return;
  if (window.__babel && window.__babel.parar) window.__babel.parar();   // uma versão antiga sai de cena

  const PALAVRAS_MINIMAS = __PALAVRAS_MINIMAS__;
  const MAX_LOTE_TEXTOS = 40, MAX_LOTE_CARACTERES = 12000, MAX_TEXTO = 15000;
  const INLINE = new Set(['A', 'ABBR', 'B', 'BDI', 'BDO', 'BR', 'CITE', 'DFN', 'EM', 'FONT', 'I', 'IMG', 'KBD',
    'MARK', 'Q', 'S', 'SMALL', 'SPAN', 'STRONG', 'SUB', 'SUP', 'TIME', 'U', 'VAR', 'WBR', 'CODE']);
  const PULAR = 'script,style,noscript,template,textarea,input,select,option,pre,svg,canvas,video,audio,' +
    'iframe,[contenteditable=""],[contenteditable="true"],[data-babel-nao],.babel-aviso';
  const OPACO = 'code,kbd,samp,img,svg';   // ficam onde estão, sem tradução (viram <iN/>)
  const IDIOMAS = { en: 'inglês', es: 'espanhol', fr: 'francês', de: 'alemão', it: 'italiano', ru: 'russo',
    pl: 'polonês', uk: 'ucraniano', tr: 'turco', zh: 'chinês', ja: 'japonês', ko: 'coreano', nl: 'holandês',
    sv: 'sueco', cs: 'tcheco', hu: 'húngaro', ro: 'romeno', th: 'tailandês', vi: 'vietnamita', ar: 'árabe' };

  const B = window.__babel = { versao: VERSAO, ligado: true, unidades: new Set(), traduzidas: 0 };
  const doNo = new WeakMap();          // nó de texto -> unidade que cuida dele
  const vistos = new WeakSet();        // nós de texto já avaliados (português, curto demais...)
  const porElemento = new WeakMap();   // elemento pai -> Set de unidades (para o Alt+clique)
  const memoria = new Map();           // fonte -> resposta: a mesma frase de novo não vai nem ao serviço
  const pedidos = new Map();           // id do pedido -> unidades
  let fila = [], seq = 0, agendado = false;

  const enviar = (obj) => { try { window.__babelSend(JSON.stringify(obj)); } catch (e) { /* binding sumiu */ } };
  const avisoLog = (msg) => enviar({ tipo: 'log', msg });

  // ---------- 1. Português? (pular sem gastar nada) ----------
  // Palavras que existem no português e não no espanhol/inglês (as ambíguas, como "de" e "a", ficam de fora).
  const PT = new Set(('não você vocês está estão estava também então muito muita muitos isso isto esse essa ' +
    'estes uma umas pelo pela pelos pelas nós às até jogo jogos jogar jogando jogado tem têm em sempre ' +
    'foi foram são com pra mas porque quando ainda aqui ele ela eles elas meu minha meus minhas seu sua seus ' +
    'suas dos das nos nas num numa já só bem agora depois ao aos ou ser ter fazer pode podem vez vezes ' +
    'nenhum nenhuma algum alguma outro outra após hoje ontem semana dia dias ano anos pessoas cheio ' +
    'pessoa análise análises publicada publicado útil engraçada acharam achou registradas recomendado ' +
    'recomendados comentário comentários onde qual quais sem sim ' +
    'janeiro fevereiro março junho julho setembro outubro novembro dezembro conquista ' +
    'conquistas biblioteca loja comunidade atualização atualizações notícias eventos grátis desconto ' +
    'oficina guia guias discussões tópico tópicos resposta respostas mensagem mensagens ' +
    'obrigado obrigada valeu galera mano coisa coisas bom ruim melhor pior').split(' '));
  const ES = new Set(('el los las pero muy hay también eso esto una del al y es juego juegos tiene fue son ' +
    'este esta estas todos todas todo toda lo le les muchos muchas encanta recomiendo ' +
    'con por cuando todavía aquí yo mi su ya solo ahora después enero febrero marzo junio julio septiembre ' +
    'octubre noviembre diciembre gracias amigos bueno malo mejor peor cosa cosas usted ustedes').split(' '));
  function pareceBR(texto) {
    const palavras = texto.toLowerCase().match(/\p{L}+/gu) || [];
    if (!palavras.length) return false;
    let pt = 0, es = 0;
    for (const p of palavras) {
      // O pretérito em -ou (jogou, acertou, gostou) é uma marca forte do português.
      if (PT.has(p) || (p.length >= 5 && p.endsWith('ou'))) pt++;
      if (ES.has(p)) es++;
    }
    if (/[ãõ]|ção|ções/i.test(texto)) pt += 2;
    if (/[ñ¿¡]/.test(texto)) es += 2;
    const taxa = pt / palavras.length;
    return pt > es && ((pt >= 1 && taxa >= 0.25) || (pt >= 4 && taxa >= 0.12));
  }

  // Nome de jogo, DLC ou edição ("Metro: Last Light Redux"): curto e com todas as palavras em maiúscula.
  // Traduzir isso só gastaria API (e um tradutor ruim ainda estragaria o nome).
  const CONECTIVOS = new Set(['of', 'the', 'and', 'a', 'an', 'to', 'in', 'for', 'on', 'at', 'with', 'vs', 'de', 'la', 'le', 'du', 'des', 'der', 'die', 'das', 'und']);
  function pareceTitulo(texto) {
    const palavras = texto.split(/[\s:—–\-|/]+/).filter((p) => /\p{L}/u.test(p));
    if (!palavras.length || palavras.length > 6) return false;
    return palavras.every((p) => CONECTIVOS.has(p.toLowerCase()) || /^[^\p{L}]*[\p{Lu}\d]/u.test(p));
  }

  // ---------- 2. Achar as unidades (um trecho de texto corrido dentro de um bloco) ----------
  const ehOpaco = (el) => el.matches(OPACO);
  function soInline(el) {
    if (!INLINE.has(el.tagName) || el.matches(PULAR)) return false;
    if (ehOpaco(el)) return true;
    for (const filho of el.childNodes) {
      if (filho.nodeType === 1 && !soInline(filho)) return false;
    }
    return true;
  }
  const entraNaCorrida = (no) => no.nodeType === 3 || (no.nodeType === 1 && soInline(no));

  function serializar(nos) {
    // Vira texto com marcas: <tN>…</tN> para elementos com texto dentro, <iN/> para os opacos e <br/>.
    let s = '', n = 0;
    const esq = [], segs = [[]], ancoras = [];
    const esc = (t) => t.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    const marca = (m, ancora) => { esq.push(m); ancoras.push(ancora); segs.push([]); };
    const vai = (no) => {
      if (no.nodeType === 3) { s += esc(no.nodeValue); segs[segs.length - 1].push(no); return; }
      if (no.nodeType !== 1 || no.tagName === 'WBR') return;
      if (no.tagName === 'BR') { s += '<br/>'; marca('br', { depois: no }); return; }
      const k = ++n;
      if (ehOpaco(no) || !no.firstChild) { s += `<i${k}/>`; marca('i' + k, { depois: no }); return; }
      s += `<t${k}>`; marca('t' + k, { dentro: no });
      no.childNodes.forEach(vai);
      s += `</t${k}>`; marca('/t' + k, { depois: no });
    };
    nos.forEach(vai);
    return { s, esq, segs, ancoras };
  }

  function analisar(t) {
    const des = (x) => x.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
    const esq = [], textos = [];
    let ultimo = 0, m;
    const re = /<(\/?)t(\d+)>|<i(\d+)\s*\/>|<br\s*\/?>/g;
    while ((m = re.exec(t))) {
      textos.push(des(t.slice(ultimo, m.index)));
      esq.push(m[2] ? (m[1] ? '/t' : 't') + m[2] : m[3] ? 'i' + m[3] : 'br');
      ultimo = re.lastIndex;
    }
    textos.push(des(t.slice(ultimo)));
    return { esq, textos };
  }

  function contaPalavras(texto) {
    const cjk = (texto.match(/[\p{Script=Han}\p{Script=Hiragana}\p{Script=Katakana}\p{Script=Hangul}\p{Script=Thai}]/gu) || []).length;
    return (texto.match(/\p{L}{2,}/gu) || []).length + Math.floor(cjk / 2);
  }

  function criarUnidade(pai, nos) {
    const ser = serializar(nos);
    const plano = ser.segs.flat().map((n) => n.nodeValue).join(' ').replace(/\s+/g, ' ').trim();
    ser.segs.flat().forEach((n) => vistos.add(n));
    if (!plano || plano.length > MAX_TEXTO || contaPalavras(plano) < PALAVRAS_MINIMAS || pareceBR(plano)) return;
    if (!/\p{L}/u.test(plano)) return;
    // Um "texto" sem nenhum espaço é endereço, código ou nome de arquivo (o CJK não usa espaço, então passa).
    if (!/\s/.test(plano) && contaPalavras(plano.replace(/[^\p{Script=Han}\p{Script=Hiragana}\p{Script=Katakana}\p{Script=Hangul}\p{Script=Thai}]/gu, '')) < PALAVRAS_MINIMAS) return;
    if (/^(https?:\/\/|www\.)\S+$/i.test(plano)) return;
    if (pareceTitulo(plano)) return;
    const u = { pai, ...ser, fonte: ser.s.trim(), estado: 'nova', orig: new Map(), criados: [], idioma: null };
    ser.segs.flat().forEach((n) => { doNo.set(n, u); u.orig.set(n, n.nodeValue); });
    B.unidades.add(u);
    if (!porElemento.has(pai)) porElemento.set(pai, new Set());
    porElemento.get(pai).add(u);
    observarVisivel(pai);
  }

  function varrer(raiz) {
    if (!raiz || !raiz.isConnected) return;
    if (raiz.nodeType === 3) raiz = raiz.parentNode;
    if (raiz.nodeType !== 1 || raiz.closest(PULAR)) return;
    const caminhante = document.createTreeWalker(raiz, NodeFilter.SHOW_TEXT, {
      acceptNode: (n) => (!n.nodeValue.trim() || vistos.has(n) || doNo.has(n)) ? NodeFilter.FILTER_REJECT
        : NodeFilter.FILTER_ACCEPT,
    });
    const iniciais = [];
    for (let n = caminhante.nextNode(); n; n = caminhante.nextNode()) iniciais.push(n);
    for (const texto of iniciais) {
      if (vistos.has(texto) || doNo.has(texto) || !texto.isConnected) continue;
      const pai0 = texto.parentElement;
      if (!pai0 || pai0.closest(PULAR)) { vistos.add(texto); continue; }
      // Sobe pelos elementos inline até o bloco que contém o trecho.
      let topo = texto;
      while (topo.parentElement && topo.parentElement !== document.body && soInline(topo.parentElement)) {
        topo = topo.parentElement;
      }
      const pai = topo.parentElement;
      if (!pai) continue;
      // A corrida: os irmãos inline/texto em volta, até esbarrar num bloco.
      let ini = topo, fim = topo;
      while (ini.previousSibling && entraNaCorrida(ini.previousSibling)) ini = ini.previousSibling;
      while (fim.nextSibling && entraNaCorrida(fim.nextSibling)) fim = fim.nextSibling;
      const nos = [];
      for (let n = ini; n; n = n.nextSibling) { nos.push(n); if (n === fim) break; }
      criarUnidade(pai, nos);
    }
  }

  // ---------- 3. Só o que está na tela (ou perto) vai para a fila ----------
  const ADIANTAR = 12;
  const visibilidade = new IntersectionObserver((entradas) => {
    let algum = false;
    for (const e of entradas) {
      if (!e.isIntersecting) continue;
      visibilidade.unobserve(e.target);
      for (const u of porElemento.get(e.target) || []) if (u.estado === 'nova') { enfileirar(u); algum = true; }
    }
    // Adianta os próximos trechos (na ordem da página): dentro de janelas que rolam, como a da nota de
    // atualização, o "perto da tela" não vale, e assim o texto já chega traduzido quando você rola.
    if (algum) {
      let faltam = ADIANTAR, depois = false;
      for (const u of B.unidades) {
        if (u.estado === 'pedida') depois = true;
        else if (depois && u.estado === 'nova' && u.pai.isConnected) { enfileirar(u); if (--faltam === 0) break; }
      }
    }
  }, { rootMargin: '1200px 0px' });
  const observarVisivel = (el) => visibilidade.observe(el);

  function enfileirar(u) {
    if (!B.ligado) return;
    const pronto = memoria.get(u.fonte);
    if (pronto) { aplicar(u, pronto); return; }
    u.estado = 'pedida';
    fila.push(u);
    if (!agendado) { agendado = true; setTimeout(despachar, 80); }
  }

  function despachar() {
    agendado = false;
    if (!permitido(location)) { fila.forEach((u) => { u.estado = 'nova'; }); fila = []; return; }
    while (fila.length) {
      const lote = [];
      let tamanho = 0;
      while (fila.length && lote.length < MAX_LOTE_TEXTOS &&
             (!lote.length || tamanho + fila[0].fonte.length <= MAX_LOTE_CARACTERES)) {
        const u = fila.shift();
        if (!u.pai.isConnected) { u.estado = 'nova'; continue; }
        lote.push(u); tamanho += u.fonte.length;
      }
      if (!lote.length) continue;
      const id = ++seq;
      pedidos.set(id, lote);
      enviar({ tipo: 'traduzir', id, textos: lote.map((u) => u.fonte) });
    }
  }

  B.receber = (resposta) => {
    const lote = pedidos.get(resposta.id);
    if (!lote) return;
    pedidos.delete(resposta.id);
    lote.forEach((u, i) => {
      const item = resposta.itens[i];
      if (!item) { u.estado = 'falhou'; return; }
      if (memoria.size > 3000) memoria.delete(memoria.keys().next().value);
      memoria.set(u.fonte, item);
      aplicar(u, item);
    });
    silenciar();
    avisar();
  };

  // ---------- 4. Trocar o texto no lugar ----------
  function preserva(original, novo) {
    // Mantém os espaços e quebras das pontas do texto original (a página pode depender deles).
    const ini = original.match(/^\s*/)[0], fim = original.match(/\s*$/)[0];
    return ini + novo.trim() + fim;
  }

  function aplicar(u, item) {
    if (item.l === 'pt' || !u.pai.isConnected) { u.estado = 'pt'; return; }
    if (item.t.trim() === u.fonte) { u.estado = 'igual'; return; }   // nada mudou: não marca
    const { esq, textos } = analisar(item.t);
    const iguais = esq.length === u.esq.length && esq.every((m, i) => m === u.esq[i]);
    u.novos = new Map();
    if (iguais) {
      textos.forEach((txt, i) => {
        const nos = u.segs[i];
        if (nos.length) {
          const original = nos.map((n) => u.orig.get(n)).join('');
          nos.forEach((n, j) => u.novos.set(n, j === 0 ? preserva(original, txt) : ''));
        } else if (txt.trim()) {
          // A tradução pôs texto onde o original não tinha (ex.: antes de um link): cria o nó.
          const novo = document.createTextNode(txt);
          const ancora = u.ancoras[i - 1];
          if (ancora.dentro) ancora.dentro.insertBefore(novo, ancora.dentro.firstChild);
          else ancora.depois.parentNode && ancora.depois.parentNode.insertBefore(novo, ancora.depois.nextSibling);
          u.criados.push(novo); vistos.add(novo); doNo.set(novo, u);
          u.orig.set(novo, ''); u.novos.set(novo, txt);
        }
      });
    } else {
      // As marcas vieram fora de ordem: põe a tradução inteira no primeiro trecho, sem formatação.
      const plano = textos.join(' ').replace(/\s+/g, ' ').trim();
      const todos = u.segs.flat();
      todos.forEach((n, j) => u.novos.set(n, j === 0 ? preserva(u.orig.get(n), plano) : ''));
    }
    u.idioma = item.l;
    u.estado = 'traduzida';
    if (B.ligado) mostrar(u, true);
    B.traduzidas++;
  }

  function mostrar(u, traduzido) {
    if (!u.novos) return;
    for (const [n, valor] of u.novos) n.nodeValue = traduzido ? valor : u.orig.get(n);
    u.mostrando = traduzido;
    if (traduzido) {
      u.pai.setAttribute('data-babel', u.idioma);
      if (!u.pai.title) u.pai.title = `Traduzido do ${IDIOMAS[u.idioma] || u.idioma} pelo BABEL · Alt+clique mostra o original`;
    } else {
      u.pai.setAttribute('data-babel', 'original');
    }
  }

  // ---------- 5. Vigiar a página (conteúdo novo, rolagem infinita, React redesenhando) ----------
  const pendentes = new Set();
  let varredura = null;
  const observador = new MutationObserver((registros) => {
    for (const r of registros) {
      if (r.type === 'characterData') {
        const u = doNo.get(r.target);
        if (!u) { vistos.delete(r.target); pendentes.add(r.target); continue; }
        const esperado = u.mostrando ? u.novos && u.novos.get(r.target) : u.orig.get(r.target);
        if (esperado === undefined || r.target.nodeValue === esperado) continue;
        if (r.target.nodeValue === u.orig.get(r.target)) {
          // O React redesenhou com o mesmo texto original: reaplica a tradução (de graça, já temos).
          if (u.mostrando) queueMicrotask(() => { mostrar(u, true); silenciar(); });
        } else {
          // A página trocou o conteúdo: os outros trechos da unidade voltam ao original e tudo é reavaliado.
          esquecer(u, r.target);
          pendentes.add(r.target);
        }
      } else {
        r.addedNodes.forEach((n) => pendentes.add(n));
      }
    }
    if (pendentes.size && !varredura) {
      varredura = setTimeout(() => {
        varredura = null;
        const raizes = [...pendentes];
        pendentes.clear();
        raizes.forEach(varrer);
      }, 150);
    }
  });
  const silenciar = () => observador.takeRecords();   // descarta os avisos das nossas próprias trocas

  function esquecer(u, trocado) {
    B.unidades.delete(u);
    if (u.mostrando && u.novos) {
      for (const n of u.novos.keys()) if (n !== trocado && n.isConnected) n.nodeValue = u.orig.get(n);
      silenciar();
    }
    [...u.segs.flat(), ...u.criados].forEach((n) => { doNo.delete(n); vistos.delete(n); });
    const conj = porElemento.get(u.pai);
    if (conj) conj.delete(u);
  }

  // ---------- 6. Controles: Alt+clique (um bloco) e Alt+Shift+T (a página toda) ----------
  function aoClicar(e) {
    if (!e.altKey || e.shiftKey || e.ctrlKey) return;
    const el = e.target.closest && e.target.closest('[data-babel]');
    if (!el || !porElemento.has(el)) return;
    e.preventDefault(); e.stopPropagation();
    for (const u of porElemento.get(el)) if (u.estado === 'traduzida') mostrar(u, !u.mostrando);
    silenciar();
  }

  function aoTeclar(e) {
    if (!(e.altKey && e.shiftKey && (e.key === 'T' || e.key === 't'))) return;
    e.preventDefault();
    B.ligado = !B.ligado;
    for (const u of B.unidades) {
      if (!u.pai.isConnected) { esquecer(u); continue; }
      if (u.estado === 'traduzida') mostrar(u, B.ligado);
      else if (B.ligado && u.estado === 'nova') observarVisivel(u.pai);
    }
    silenciar();
    avisar(B.ligado ? 'BABEL ligado' : 'BABEL desligado: textos originais');
  }

  document.addEventListener('click', aoClicar, true);
  document.addEventListener('keydown', aoTeclar, true);

  B.parar = () => {
    observador.disconnect();
    visibilidade.disconnect();
    clearTimeout(varredura);
    document.removeEventListener('click', aoClicar, true);
    document.removeEventListener('keydown', aoTeclar, true);
    for (const u of B.unidades) {
      if (u.mostrando) mostrar(u, false);
      if (u.pai.isConnected) u.pai.removeAttribute('data-babel');
    }
    B.receber = () => {};
  };

  // ---------- 7. O aviso discreto e a marca visual ----------
  let avisoTimer = null, avisado = false;
  function avisar(texto) {
    if (!texto && avisado) return;
    avisado = true;
    const raiz = document.documentElement;
    let caixa = raiz.querySelector(':scope > .babel-aviso');
    if (!caixa) {
      caixa = document.createElement('div');
      caixa.className = 'babel-aviso';
      raiz.appendChild(caixa);
    }
    caixa.textContent = texto || '🌐 BABEL traduziu esta página · Alt+clique num trecho mostra o original · Alt+Shift+T liga/desliga';
    caixa.style.opacity = '1';
    clearTimeout(avisoTimer);
    avisoTimer = setTimeout(() => { caixa.style.opacity = '0'; }, 4500);
  }

  function estilo() {
    if (document.getElementById('babel-estilo')) return;
    const css = document.createElement('style');
    css.id = 'babel-estilo';
    css.textContent = `
      [data-babel]:not([data-babel="original"]) { box-shadow: inset 2px 0 0 rgba(102,192,244,.45); }
      [data-babel="original"] { box-shadow: inset 2px 0 0 rgba(244,180,102,.7); }
      .babel-aviso { position: fixed; right: 16px; bottom: 16px; z-index: 2147483647; max-width: 420px;
        padding: 8px 12px; border-radius: 6px; background: rgba(23,26,33,.94); color: #c7d5e0;
        font: 12px/1.4 "Motiva Sans", Arial, sans-serif; box-shadow: 0 4px 16px rgba(0,0,0,.5);
        pointer-events: none; opacity: 0; transition: opacity .4s; }`;
    (document.head || document.documentElement).appendChild(css);
  }

  // ---------- Partida ----------
  function partir() {
    estilo();
    observador.observe(document.body, { childList: true, subtree: true, characterData: true });
    varrer(document.body);
  }
  if (document.body) partir();
  else document.addEventListener('DOMContentLoaded', partir, { once: true });
})();
