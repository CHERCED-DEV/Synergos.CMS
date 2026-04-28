/**
 * host-bridge contract — Vitest harness (Cap-250 Batch B, Ola 243)
 *
 * Valida que la shape de `window.synergos` cumpla el contrato
 * documentado en
 * `Synergos.CMS/Synergos.CMS.Web/docs/contracts/host-bridge.md`.
 *
 * Estos tests NO instancian el bridge desde el CMS — armaron un
 * mock de `window.synergos` con la shape canónica + verifica que
 * los helpers consumer-side (`getBridge()`, `t()`, `getMember()`,
 * `getTheme()`) se comporten según el contrato.
 */

import { afterEach, describe, it, expect } from 'vitest';

interface SynergosI18n {
    readonly culture: string;
    readonly defaultCulture: string;
    readonly keys: Record<string, string>;
    t(key: string, fallback?: string): string;
}

interface SynergosTheme {
    readonly variant: string;
    readonly available: readonly string[];
}

interface SynergosBrand {
    readonly key: string;
    readonly displayName: string;
}

interface SynergosMember {
    readonly key: string;
    readonly displayName: string;
    readonly email: string;
    readonly roles: readonly string[];
}

interface SynergosPage {
    readonly id: number;
    readonly docType: string;
    readonly canonicalUrl: string;
    readonly cultures: readonly string[];
}

interface SynergosBridge {
    readonly version: string;
    readonly i18n: SynergosI18n;
    readonly theme: SynergosTheme;
    readonly brand: SynergosBrand;
    readonly member: SynergosMember | null;
    readonly page: SynergosPage;
}

declare global {
    interface Window {
        synergos?: SynergosBridge;
    }
}

function buildBridge(overrides: Partial<SynergosBridge> = {}): SynergosBridge {
    const keys = overrides.i18n?.keys ?? {
        'Form.Submit': 'Enviar',
        'Form.Cancel': 'Cancelar',
        'Common.Loading': 'Cargando...',
    };
    return {
        version: '1.0.0',
        i18n: {
            culture: 'es-CO',
            defaultCulture: 'es-CO',
            keys,
            t(key: string, fallback?: string): string {
                return keys[key] !== undefined
                    ? keys[key]
                    : fallback !== undefined
                        ? fallback
                        : key;
            },
        },
        theme: { variant: 'light', available: ['light', 'dark'] },
        brand: { key: 'default', displayName: 'Synergos' },
        member: null,
        page: {
            id: 1042,
            docType: 'pageStandard',
            canonicalUrl: 'https://synergos.test/post/welcome',
            cultures: ['es-CO', 'en-US'],
        },
        ...overrides,
    };
}

// Helpers consumer-side mirrored del UI repo (vitals/runtime/synergos-bridge.ts).
function getBridge(): SynergosBridge | null {
    return typeof window !== 'undefined' && window.synergos
        ? window.synergos
        : null;
}

function t(key: string, fallback: string): string {
    const bridge = getBridge();
    return bridge?.i18n?.t?.(key, fallback) ?? fallback;
}

function getMember(): SynergosMember | null {
    return getBridge()?.member ?? null;
}

function getTheme(): string {
    return getBridge()?.theme?.variant ?? 'light';
}

afterEach(() => {
    delete window.synergos;
});

describe('window.synergos shape', () => {
    it('expone version + i18n + theme + brand + member + page', () => {
        window.synergos = buildBridge();
        const bridge = getBridge();

        expect(bridge).not.toBeNull();
        expect(bridge!.version).toMatch(/^\d+\.\d+\.\d+$/);
        expect(typeof bridge!.i18n).toBe('object');
        expect(typeof bridge!.theme).toBe('object');
        expect(typeof bridge!.brand).toBe('object');
        expect(typeof bridge!.page).toBe('object');
    });

    it('member null para anónimos', () => {
        window.synergos = buildBridge({ member: null });
        expect(getMember()).toBeNull();
    });

    it('member shape para autenticados', () => {
        window.synergos = buildBridge({
            member: {
                key: 'abcd1234',
                displayName: 'Alice',
                email: 'alice@example.com',
                roles: ['member', 'admin'],
            },
        });

        const m = getMember();
        expect(m).not.toBeNull();
        expect(m!.email).toBe('alice@example.com');
        expect(m!.roles).toEqual(['member', 'admin']);
    });
});

describe('getBridge() — degradación graceful', () => {
    it('returns null cuando window.synergos undefined', () => {
        expect(getBridge()).toBeNull();
    });

    it('t() returns fallback cuando bridge no existe', () => {
        expect(t('Form.Submit', 'Submit')).toBe('Submit');
    });

    it('getTheme() retorna "light" como safe default', () => {
        expect(getTheme()).toBe('light');
    });
});

describe('Theme', () => {
    it('variant + available están alineados', () => {
        window.synergos = buildBridge({
            theme: { variant: 'dark', available: ['light', 'dark', 'silvergold'] },
        });
        expect(getTheme()).toBe('dark');
        expect(getBridge()!.theme.available).toContain('dark');
    });
});

describe('Page metadata', () => {
    it('cultures es array de strings tipo es-CO / en-US', () => {
        window.synergos = buildBridge();
        const cultures = getBridge()!.page.cultures;
        expect(cultures.length).toBeGreaterThan(0);
        for (const c of cultures) {
            expect(c).toMatch(/^[a-z]{2}-[A-Z]{2}$/);
        }
    });

    it('canonicalUrl es absoluta', () => {
        window.synergos = buildBridge();
        expect(getBridge()!.page.canonicalUrl).toMatch(/^https?:\/\//);
    });
});

describe('Versioning compat check', () => {
    it('UI v1 acepta bridge v1.x.x sin warning', () => {
        window.synergos = buildBridge({ version: '1.5.2' });
        const bridge = getBridge();
        const major = parseInt(bridge!.version.split('.')[0], 10);
        expect(major).toBe(1);
    });

    it('UI debe poder detectar major bump (v2.x)', () => {
        window.synergos = buildBridge({ version: '2.0.0' });
        const bridge = getBridge();
        const major = parseInt(bridge!.version.split('.')[0], 10);
        expect(major).toBe(2);
    });
});

describe('Security boundary', () => {
    it('member NO contiene fields sensibles (passwordHash, secret, sessionToken)', () => {
        window.synergos = buildBridge({
            member: {
                key: 'abc',
                displayName: 'Alice',
                email: 'alice@example.com',
                roles: ['member'],
            },
        });
        const m = getMember();
        const keys = Object.keys(m!);
        expect(keys).not.toContain('passwordHash');
        expect(keys).not.toContain('secret');
        expect(keys).not.toContain('sessionToken');
        expect(keys).not.toContain('twoFactorSecret');
    });
});
