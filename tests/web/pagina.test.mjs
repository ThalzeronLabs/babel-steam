// Testes das regras do pagina.js que não dependem da página: o detector de português, o de títulos
// e a leitura das marcas de formatação. Rodar: node testes/teste_pagina.mjs
import { readFileSync } from 'node:fs';

const src = readFileSync(new URL('../../src/Babel/Web/pagina.js', import.meta.url), 'utf8');
const trecho = (de, ate) => src.slice(src.indexOf(de), src.indexOf(ate));
const ctx = {};
new Function('ctx', trecho('  // Palavras que existem', '  // ---------- 2.') +
  trecho('  function analisar', '  function contaPalavras') +
  '; ctx.pareceBR = pareceBR; ctx.pareceTitulo = pareceTitulo; ctx.analisar = analisar;')(ctx);

let falhas = 0;
const confere = (nome, obtido, esperado) => {
  const ok = JSON.stringify(obtido) === JSON.stringify(esperado);
  if (!ok) { falhas++; console.log('ERRO', nome, '→', JSON.stringify(obtido), '≠', JSON.stringify(esperado)); }
};

for (const [t, e] of [
  ['1.192 pessoas acharam esta análise útil', true],
  ['Publicada: 25 de janeiro de 2021', true],
  ['Este jogo é muito bom, recomendo para todos.', true],
  ['Mano esse jogo é top demais, joguei a noite toda kkkk', true],
  ['Como sempre, a Supergiant acertou em cheio', true],
  ['i played this game for maybe an hour, my gf said hey that looks fun can i have a turn', false],
  ['Im bad at video games. You unlock more story/content by losing. 10/10', false],
  ['El juego es muy bueno pero tiene muchos errores todavía', false],
  ['Me encanta este juego, lo recomiendo a todos', false],
  ['Der Bosskampf ist viel zu schwer und die Hitbox ist kaputt', false],
  ['Patch notes: fixed a crash when loading saves', false],
]) confere(`pareceBR(${t})`, ctx.pareceBR(t), e);

for (const [t, e] of [
  ['Sleeping Dogs: Definitive Edition', true], ["Tom Clancy's Ghost Recon® Wildlands", true],
  ['Metro: Last Light Redux', true], ['The Last of Us Part I', true],
  ['Call of Duty: Modern Warfare 4 Is Live Worldwide — Play Now!', false],
  ['This update fixes the crash on startup', false],
]) confere(`pareceTitulo(${t})`, ctx.pareceTitulo(t), e);

confere('analisar com marcas', ctx.analisar('O <t1>chefe</t1> caiu<br/>de novo <i2/> &lt;3'),
  { esq: ['t1', '/t1', 'br', 'i2'], textos: ['O ', 'chefe', ' caiu', 'de novo ', ' <3'] });
confere('analisar sem marcas', ctx.analisar('só texto'), { esq: [], textos: ['só texto'] });

console.log(falhas ? `${falhas} falha(s)` : 'todos os testes do pagina.js passaram');
process.exit(falhas ? 1 : 0);
