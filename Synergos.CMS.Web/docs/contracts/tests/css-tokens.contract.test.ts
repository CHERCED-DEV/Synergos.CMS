/**
 * css-tokens contract — Vitest harness (Cap-250 Batch B, Ola 245)
 *
 * Valida naming convention + categorías canónicas de las CSS custom
 * properties con prefix `--syn-*` per
 * `Synergos.CMS/Synergos.CMS.Web/docs/contracts/css-tokens.md`.
 *
 * Tests son shape-only (no leen el syn-tokens.css real). Para
 * validation contra el archivo source, agregar fs read en CI step.
 */

import { describe, it, expect } from 'vitest';

const TOKEN_PATTERN = /^--syn-[a-z0-9]+(-[a-z0-9]+)*$/;

const COLOR_FAMILY_SHADE = /^--syn-color-(neutral|brand|accent)-(0|50|100|200|300|400|500|600|700|800|900|950)$/;

const SEMANTIC_COLORS = [
    /^--syn-color-text-(primary|secondary|muted|inverse|accent|success|warn|danger|info)$/,
    /^--syn-color-surface-(base|raised|sunken|inverse|brand|accent|success|warn|danger|info)$/,
    /^--syn-color-border-(default|subtle|strong|inverse|brand|focus)$/,
    /^--syn-color-state-(success|warn|danger|info)$/,
    /^--syn-color-action-[a-z]+(-[a-z]+)*$/,
];

const SPACING_PATTERN = /^--syn-space-(xs|sm|md|lg|xl|2xl|3xl|4xl)$/;

const TYPO_PATTERNS = [
    /^--syn-font-family-(heading|body|mono)$/,
    /^--syn-font-size-(xs|sm|base|md|lg|xl|2xl|3xl)$/,
    /^--syn-font-weight-(regular|medium|semibold|bold)$/,
    /^--syn-line-height-(tight|normal|relaxed)$/,
];

const RADIUS_PATTERN = /^--syn-radius-(none|sm|md|lg|full)$/;

describe('Token naming convention', () => {
    const validTokens = [
        '--syn-color-brand-500',
        '--syn-color-text-primary',
        '--syn-space-md',
        '--syn-font-family-heading',
        '--syn-radius-lg',
        '--syn-line-height-tight',
    ];

    const invalidTokens = [
        '--syncolor-brand-500',           // missing dash separator
        '--SYN-color-brand-500',          // uppercase
        '--syn-color-Brand-500',          // PascalCase shade family
        '--syn_color_brand_500',          // underscore
        '--brand-500',                    // missing syn prefix
    ];

    it('todos los tokens válidos matchean el pattern --syn-*', () => {
        for (const t of validTokens) {
            expect(t).toMatch(TOKEN_PATTERN);
        }
    });

    it('tokens inválidos NO matchean', () => {
        for (const t of invalidTokens) {
            expect(t).not.toMatch(TOKEN_PATTERN);
        }
    });
});

describe('Color tokens — primitive scale', () => {
    it('familias canónicas: neutral, brand, accent', () => {
        const valid = [
            '--syn-color-neutral-50',
            '--syn-color-neutral-950',
            '--syn-color-brand-500',
            '--syn-color-accent-700',
        ];
        for (const t of valid) {
            expect(t).toMatch(COLOR_FAMILY_SHADE);
        }
    });

    it('shades válidos: 0, 50, 100..900, 950', () => {
        expect('--syn-color-brand-50').toMatch(COLOR_FAMILY_SHADE);
        expect('--syn-color-brand-950').toMatch(COLOR_FAMILY_SHADE);
        expect('--syn-color-brand-500').toMatch(COLOR_FAMILY_SHADE);
    });

    it('shades inválidos rechazados', () => {
        expect('--syn-color-brand-450').not.toMatch(COLOR_FAMILY_SHADE);
        expect('--syn-color-brand-1000').not.toMatch(COLOR_FAMILY_SHADE);
        expect('--syn-color-purple-500').not.toMatch(COLOR_FAMILY_SHADE); // family no canon
    });
});

describe('Color tokens — semantic roles', () => {
    it('text/surface/border/state/action siguen patterns canónicos', () => {
        const cases: Record<string, RegExp> = {
            '--syn-color-text-primary': SEMANTIC_COLORS[0],
            '--syn-color-text-muted': SEMANTIC_COLORS[0],
            '--syn-color-surface-base': SEMANTIC_COLORS[1],
            '--syn-color-surface-raised': SEMANTIC_COLORS[1],
            '--syn-color-border-default': SEMANTIC_COLORS[2],
            '--syn-color-border-focus': SEMANTIC_COLORS[2],
            '--syn-color-state-success': SEMANTIC_COLORS[3],
            '--syn-color-state-danger': SEMANTIC_COLORS[3],
            '--syn-color-action-primary-hover': SEMANTIC_COLORS[4],
        };
        for (const [token, pattern] of Object.entries(cases)) {
            expect(token).toMatch(pattern);
        }
    });
});

describe('Spacing tokens — escala xs..4xl', () => {
    it('matchea pattern --syn-space-{size}', () => {
        const sizes = ['xs', 'sm', 'md', 'lg', 'xl', '2xl', '3xl', '4xl'];
        for (const s of sizes) {
            expect(`--syn-space-${s}`).toMatch(SPACING_PATTERN);
        }
    });

    it('sizes fuera de escala rechazados', () => {
        expect('--syn-space-tiny').not.toMatch(SPACING_PATTERN);
        expect('--syn-space-huge').not.toMatch(SPACING_PATTERN);
        expect('--syn-space-5xl').not.toMatch(SPACING_PATTERN);
    });
});

describe('Typography tokens', () => {
    it('font-family roles: heading/body/mono', () => {
        expect('--syn-font-family-heading').toMatch(TYPO_PATTERNS[0]);
        expect('--syn-font-family-body').toMatch(TYPO_PATTERNS[0]);
        expect('--syn-font-family-mono').toMatch(TYPO_PATTERNS[0]);
        expect('--syn-font-family-display').not.toMatch(TYPO_PATTERNS[0]);
    });

    it('font-weight: regular/medium/semibold/bold', () => {
        expect('--syn-font-weight-regular').toMatch(TYPO_PATTERNS[2]);
        expect('--syn-font-weight-bold').toMatch(TYPO_PATTERNS[2]);
        expect('--syn-font-weight-extrabold').not.toMatch(TYPO_PATTERNS[2]);
    });

    it('line-height: tight/normal/relaxed', () => {
        expect('--syn-line-height-tight').toMatch(TYPO_PATTERNS[3]);
        expect('--syn-line-height-loose').not.toMatch(TYPO_PATTERNS[3]);
    });
});

describe('Border + radius tokens', () => {
    it('--syn-radius-* canónico: none/sm/md/lg/full', () => {
        const valid = ['none', 'sm', 'md', 'lg', 'full'];
        for (const v of valid) {
            expect(`--syn-radius-${v}`).toMatch(RADIUS_PATTERN);
        }
        expect('--syn-radius-pill').not.toMatch(RADIUS_PATTERN);
    });
});

describe('Token consumption pattern (UI side)', () => {
    it('UI siempre declara fallback defensivo', () => {
        // Pattern requerido por contrato: var(--syn-X, fallback)
        const goodCss = 'background: var(--syn-color-brand-500, #6366f1);';
        const badCss = 'background: var(--syn-color-brand-500);';

        const fallbackPattern = /var\(\s*--syn-[a-z0-9-]+\s*,\s*[^)]+\)/;
        expect(goodCss).toMatch(fallbackPattern);
        expect(badCss).not.toMatch(fallbackPattern);
    });

    it('CMS nunca declara fallback (es source of truth)', () => {
        // En syn-tokens.css del CMS, los tokens son declaraciones, no consumo.
        const cmsRule = '--syn-color-brand-500: #6366f1;';
        const consumePattern = /var\(/;
        expect(cmsRule).not.toMatch(consumePattern);
    });
});

describe('Theme override pattern', () => {
    it('tokens overrideables via [data-theme="dark"]', () => {
        // CMS puede declarar override sets. Pattern es válido a nivel CSS:
        const lightSelector = ':root';
        const darkSelector = ':root[data-theme="dark"]';
        const compoundDarkSelector = ':root[data-theme="dark"], [data-theme="dark"]';

        expect(lightSelector).toContain(':root');
        expect(darkSelector).toContain('data-theme');
        expect(compoundDarkSelector).toContain(',');
    });
});
