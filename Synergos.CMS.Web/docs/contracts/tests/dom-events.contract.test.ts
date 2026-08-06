/**
 * dom-events contract — Vitest harness (Cap-240 Batch D, Ola 238)
 *
 * Valida que los CustomEvents emitidos por componentes <synergos-*>
 * cumplan el contrato documentado en
 * `Synergos.CMS/Synergos.CMS.Web/docs/contracts/dom-events.md`.
 *
 * Estos tests NO dependen de la implementación real de los Web
 * Components — usan CustomEvent directamente para verificar que la
 * SHAPE del contrato sea consumible. Los tests de la implementación
 * viven en Synergos.UI (mirror project, repo separado).
 */

import { describe, it, expect } from 'vitest';

type SynOutcome = 'success' | 'failure' | 'partial';

interface SynComponentReadyDetail {
    tag: string;
    version: string;
}

interface SynComponentErrorDetail {
    tag: string;
    message: string;
    error?: unknown;
}

interface SynFormStepperSubmittedDetail {
    values: Record<string, unknown>;
    outcome: SynOutcome;
}

describe('syn:component:ready (canonical lifecycle)', () => {
    it('event name follows naming convention syn:{component}:{event}', () => {
        const eventName = 'syn:component:ready';
        expect(eventName.startsWith('syn:')).toBe(true);
        const parts = eventName.split(':');
        expect(parts).toHaveLength(3);
        expect(parts[0]).toBe('syn');
    });

    it('dispatches with required detail shape { tag, version }', async () => {
        const detail: SynComponentReadyDetail = {
            tag: 'synergos-accordion',
            version: '1.0.0',
        };

        const captured = await new Promise<CustomEvent<SynComponentReadyDetail>>((resolve) => {
            document.addEventListener('syn:component:ready', (e) => resolve(e as CustomEvent<SynComponentReadyDetail>), { once: true });
            document.dispatchEvent(
                new CustomEvent<SynComponentReadyDetail>('syn:component:ready', {
                    detail,
                    bubbles: true,
                    composed: true,
                    cancelable: false,
                }),
            );
        });

        expect(captured.detail.tag).toBe('synergos-accordion');
        expect(captured.detail.version).toBe('1.0.0');
        expect(captured.bubbles).toBe(true);
        expect(captured.composed).toBe(true);
        expect(captured.cancelable).toBe(false);
    });

    it('tag must follow synergos-* prefix per ADR 0083', () => {
        const validTags = ['synergos-accordion', 'synergos-form-stepper', 'synergos-modal'];
        const invalidTags = ['accordion', 'syn-accordion', 'cdn-accordion'];

        for (const tag of validTags) {
            expect(tag.startsWith('synergos-')).toBe(true);
        }
        for (const tag of invalidTags) {
            expect(tag.startsWith('synergos-')).toBe(false);
        }
    });
});

describe('syn:component:error (canonical lifecycle)', () => {
    it('detail shape includes tag + message; error opcional', async () => {
        const detail: SynComponentErrorDetail = {
            tag: 'synergos-form-stepper',
            message: 'Hydration failed: missing data-step',
            error: new Error('boom'),
        };

        const captured = await new Promise<CustomEvent<SynComponentErrorDetail>>((resolve) => {
            document.addEventListener('syn:component:error', (e) => resolve(e as CustomEvent<SynComponentErrorDetail>), { once: true });
            document.dispatchEvent(
                new CustomEvent<SynComponentErrorDetail>('syn:component:error', {
                    detail,
                    bubbles: true,
                    composed: true,
                }),
            );
        });

        expect(captured.detail.tag).toBe('synergos-form-stepper');
        expect(captured.detail.message).toContain('Hydration failed');
    });
});

describe('Outcome tri-state enum', () => {
    it('only success | failure | partial are valid', () => {
        const valid: SynOutcome[] = ['success', 'failure', 'partial'];
        for (const v of valid) {
            expect(['success', 'failure', 'partial']).toContain(v);
        }
    });

    it('form submission propagates outcome on syn:form-stepper:submitted', async () => {
        const detail: SynFormStepperSubmittedDetail = {
            values: { name: 'Alice', email: 'alice@example.com' },
            outcome: 'success',
        };

        const captured = await new Promise<CustomEvent<SynFormStepperSubmittedDetail>>((resolve) => {
            document.addEventListener('syn:form-stepper:submitted', (e) => resolve(e as CustomEvent<SynFormStepperSubmittedDetail>), { once: true });
            document.dispatchEvent(
                new CustomEvent<SynFormStepperSubmittedDetail>('syn:form-stepper:submitted', {
                    detail,
                    bubbles: true,
                    composed: true,
                }),
            );
        });

        expect(captured.detail.outcome).toBe('success');
        expect(['success', 'failure', 'partial']).toContain(captured.detail.outcome);
    });
});

describe('Bubbling + composition (default contract)', () => {
    it('events bubble through DOM hierarchy', async () => {
        const main = document.createElement('main');
        main.id = 'content';
        main.setAttribute('data-synergos-host', '');
        const child = document.createElement('div');
        child.id = 'child-host';
        main.appendChild(child);
        document.body.appendChild(main);

        const seen: string[] = [];
        document.addEventListener('syn:accordion:opened', () => seen.push('document'));
        main.addEventListener('syn:accordion:opened', () => seen.push('main'));

        child.dispatchEvent(
            new CustomEvent('syn:accordion:opened', {
                detail: { id: 'panel-1' },
                bubbles: true,
                composed: true,
            }),
        );

        expect(seen).toEqual(['main', 'document']);

        document.body.removeChild(main);
    });
});
