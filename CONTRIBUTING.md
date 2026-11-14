# Contribuir

Obrigado pelo interesse. As contribuições mais úteis agora:

1. **Traduções estranhas:** abra uma [issue](https://github.com/ThalzeronLabs/babel-steam/issues) com o texto original,
   a tradução e o que você esperava. Muitas vezes a correção é uma linha no
   [glossário](src/Babel/Web/glossario.txt).
2. **Páginas da Steam que não traduzem ou que quebram:** diga qual página e o que aconteceu. Não mande print com o seu
   nome de usuário, amigos ou mensagens.

Para código:

- `dotnet test tests/Babel.Tests` e `node tests/web/pagina.test.mjs` precisam passar, e o build trata avisos como erros.
- Nada pode mandar a chave, o texto traduzido ou dados da pessoa para outro lugar além da API do Gemini.
- O script da página troca só o conteúdo dos nós de texto: nunca recria elementos (a biblioteca e o chat são React).
- PRs pequenos, com uma descrição clara, são os mais fáceis de revisar.
