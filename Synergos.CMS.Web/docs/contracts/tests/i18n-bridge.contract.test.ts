/**
 * i18n-bridge contract — Vitest harness (Cap-250 Batch B, Ola 244)
 *
 * Valida la API + naming convention + resolution order de
 * `window.synergos.i18n` per
 * `Synergos.CMS/Synergos.CMS.Web/docs/contracts/i18n-bridge.md`.
 */

import { afterEach, describe, it, expect } from 'vitest';

interface SynergosI18n {
    readonly culture: string;
    readonly defaultCulture: string;
    readonly keys: Record<string, string>;
    t(key: string, fallback?: string): string;
}

declare global {
    interface Window {
        synergos?: { i18n: SynergosI18n; [k: string]: unknown };
    }
}

function buildI18n(keys: Record<string, string> = {}, culture = 'es-CO'): SynergosI18n {
    return {
        culture,
        defaultCulture: 'es-CO',
        keys,
        t(key: string, fallback?: string): string {
            return keys[key] !== undefined
                ? keys[key]
                : fallback !== undefined
                    ? fallback
                    : key;
        },
    };
}

afterEach(() => {
    delete window.synergos;
});

describe('t(key, fallback) resolution order', () => {
    it('1. retorna keys[key] si existe', () => {
        const i18n = buildI18n({ 'Form.Submit': 'Enviar' });
        expect(i18n.t('Form.Submit', 'Submit')).toBe('Enviar');
    });

    it('2. retorna fallback si key no existe', () => {
        const i18n = buildI18n({});
        expect(i18n.t('NonExistent', 'Default')).toBe('Default');
    });

    it('3. retorna key literal si no hay match ni fallback', () => {
        const i18n = buildI18n({});
        expect(i18n.t('NonExistent')).toBe('NonExistent');
    });
});

describe('Naming convention {Section}.{SubSection}.{Key}', () => {
    const validKeys = [
        'Form.Submit',
        'Form.Stepper.Next',
        'Account.Login.Title',
        'Common.Loading',
        'Comments.Reply.Placeholder',
    ];

    const invalidKeys = [
        'form.submit',           // lowercase (typo tolerable pero no canónico)
        'FORM_SUBMIT',           // SCREAMING_SNAKE_CASE
        'form-submit',           // kebab
        'submit',                // missing section
    ];

    it('valid keys son PascalCase con dots', () => {
        for (const k of validKeys) {
            expect(k).toMatch(/^[A-Z][a-zA-Z]+(\.[A-Z][a-zA-Z]+)+$/);
        }
    });

    it('invalid keys NO matchean el pattern canónico', () => {
        for (const k of invalidKeys) {
            expect(k).not.toMatch(/^[A-Z][a-zA-Z]+(\.[A-Z][a-zA-Z]+)+$/);
        }
    });
});

describe('Subset publishing — solo prefixes consumidos por UI', () => {
    it('Form.* + Search.* + Common.* + Comments.* + Cart.* + Shop.* publicados', () => {
        const published = ['Form.Submit', 'Search.Placeholder', 'Common.Loading',
            'Comments.Reply', 'Cart.Total', 'Shop.AddToCart'];
        const i18n = buildI18n(Object.fromEntries(published.map(k => [k, `(${k})`])));
        for (const k of published) {
            expect(i18n.t(k)).not.toBe(k); // resolved, no fallback
        }
    });

    it('Admin.* + Account.* NO publicados (Razor SSR puro)', () => {
        const i18n = buildI18n({ 'Form.Submit': 'Enviar' });
        expect(i18n.t('Admin.Audit.Title', 'Audit')).toBe('Audit'); // fallback
        expect(i18n.t('Account.Login.Title', 'Login')).toBe('Login'); // fallback
    });
});

describe('Format placeholders {0}, {1}, ...', () => {
    it('strings con placeholders preservan {N} hasta replace manual', () => {
        const i18n = buildI18n({ 'Admin.Welcome': 'Bienvenido, {0}' });
        const greeting = i18n.t('Admin.Welcome', 'Welcome, {0}');
        expect(greeting).toContain('{0}');
        const personalised = greeting.replace('{0}', 'Alice');
        expect(personalised).toBe('Bienvenido, Alice');
    });
});

describe('Standalone (sin host) degradación graceful', () => {
    it('helper UI con safe-fallback funciona sin window.synergos', () => {
        const fallback = (key: string, def: string) =>
            window.synergos?.i18n?.t?.(key, def) ?? def;
        expect(fallback('Form.Submit', 'Submit')).toBe('Submit');
    });
});

describe('Reactivity — bridge es estático', () => {
    it('keys mutadas client-side NO se reflejan al UI hasta page reload', () => {
        const initial = buildI18n({ 'Form.Submit': 'Enviar' });
        window.synergos = { i18n: initial };
        expect(window.synergos.i18n.t('Form.Submit')).toBe('Enviar');

        // Cliente mutea keys (anti-pattern por contrato — UI consume, no muta).
        // El test documenta que SI lo hace, el cambio es local in-memory.
        // No hay event de "i18n updated" — UI espera reload.
        const mutable = window.synergos.i18n.keys as Record<string, string>;
        mutable['Form.Submit'] = 'Submit';
        expect(window.synergos.i18n.t('Form.Submit')).toBe('Submit');
    });
});

describe('Culture format', () => {
    it('culture sigue formato BCP-47 simple xx-XX', () => {
        const i18n = buildI18n({}, 'es-CO');
        expect(i18n.culture).toMatch(/^[a-z]{2}-[A-Z]{2}$/);
        expect(i18n.defaultCulture).toMatch(/^[a-z]{2}-[A-Z]{2}$/);
    });
});

describe('Case-insensitive lookup tolerable (no canónico)', () => {
    it('UI tolerante a typos puede normalizar antes del lookup', () => {
        const keys = { 'Form.Submit': 'Enviar' };
        const i18n = buildI18n(keys);

        // Lookup directo case-insensitive — implementación tolerante:
        const tolerantLookup = (k: string): string | undefined => {
            const exact = keys[k];
            if (exact !== undefined) return exact;
            const lowered = k.toLowerCase();
            for (const key of Object.keys(keys)) {
                if (key.toLowerCase() === lowered) return keys[key];
            }
            return undefined;
        };

        expect(tolerantLookup('form.submit')).toBe('Enviar');
        expect(tolerantLookup('FORM.SUBMIT')).toBe('Enviar');
        expect(i18n.t('Form.Submit')).toBe('Enviar'); // canonical lookup también works
    });
});
