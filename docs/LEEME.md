# Informe técnico

`informe-tecnico.pdf` — 13 páginas. Explica qué se construyó, por qué, y qué
todavía **no** está verificado. Pensado para repartirse entre las tres personas
que van a trabajar sobre esta base: la portada dice a qué secciones ir según el
rol.

La fuente es `informe-tecnico.html`. **Se edita el HTML y se vuelve a generar el
PDF, nunca al revés.** Un PDF suelto en un repositorio queda desactualizado el
día que alguien toca el código y nadie se acuerda de él; teniendo la fuente al
lado, actualizarlo son dos minutos.

## Regenerarlo

```bash
chrome --headless=new --disable-gpu --no-pdf-header-footer \
  --print-to-pdf=docs/informe-tecnico.pdf \
  docs/informe-tecnico.html
```

Cualquier navegador basado en Chromium sirve. El HTML lleva las reglas de
impresión (`@page`, saltos de página, tamaño A4) dentro, así que no hace falta
tocar nada en el diálogo de impresión.

## Qué mantener al día

Lo que más rápido envejece es la **sección 9, «Lo que NO está verificado»**. En
cuanto se aplique una migración contra un Postgres real o se pruebe una política
de aislamiento, esa tabla deja de ser cierta — y una lista de riesgos que ya no
es cierta es peor que no tener lista, porque la gente deja de leerla.
