# Changelog

## 0.1.0 (2026-11-14)

A primeira versão pública.

- Traduz para o português do Brasil, na própria tela da Steam, as notas de atualização, os eventos, as análises, as
  discussões, os guias, as descrições da Oficina e os textos da loja sem pt-BR.
- Configuração guiada em 3 passos: o que o Babel faz e o que vai para o Google; a chave grátis do Gemini, conferida
  com o Google na hora; iniciar com o Windows e ajustar a Steam.
- A chave fica cifrada pelo Windows (DPAPI), só para o seu usuário neste PC.
- Quando o Google recusa por causa da conta (o crédito pré-pago acabou, o limite foi atingido), o Babel avisa pela
  bandeja e para de pedir por um minuto. O teste da chave traduz uma palavra, e por isso uma chave sem crédito já é
  pega na configuração.
- Cache local: cada trecho é traduzido uma vez só.
- Alt + clique mostra o original; Alt + Shift + T liga e desliga a página; pausa pela bandeja.
- As travas: só a janela principal, a loja e a comunidade, nunca as páginas de conta, carteira, pagamento, compras e
  login; o chat desligado de fábrica, com aviso ao ligar; a porta conferida (tem de ser do `steamwebhelper.exe` da
  Steam) e só no 127.0.0.1; cinco comandos do DevTools e nenhum outro; limites de uso por página, por minuto e por dia;
  reabrir a Steam desligado de fábrica; mexer na Steam só com a escolha explícita da pessoa.
- `Babel.exe check`: mostra, sem injetar nada, o dono da porta e o que o Babel faria com cada página.
- Desinstalação que devolve a Steam como era e oferece reiniciá-la sem a porta.
