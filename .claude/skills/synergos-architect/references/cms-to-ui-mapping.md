# CMS Schema ↔ UI Bundle — Mapping table

> **AUTO-GENERATED** by `tools/refresh-skill-catalog.mjs`. Re-run via `npm run skill:refresh`.
>
> Esta tabla cierra el loop entre el schema CMS uSync (lo que el editor llena
> en backoffice) y el bundle UI que efectivamente hidrata en el browser.
>
> Generated: 2026-09-05T23:25:46.043Z

## Pipeline editor → bundle

```
┌─────────────────────────────────────────────────────────────────────┐
│ 1. Editor crea contenido en backoffice usando ContentType:          │
│    → Synergos.CMS.Web/uSync/v9/ContentTypes/elementsyn{name}.config │
├─────────────────────────────────────────────────────────────────────┤
│ 2. Razor partial Views/Partials/SynHost/{Pascal}.cshtml renderiza:  │
│    → ISynHostEmitter.EmitAsync(blockAlias, props)                   │
├─────────────────────────────────────────────────────────────────────┤
│ 3. IBundleRegistryClient resuelve URL del bundle:                   │
│    → /cdn-bundles/{name}/{framework}/{slot}/main.js                 │
├─────────────────────────────────────────────────────────────────────┤
│ 4. HTML emitido al browser:                                         │
│    <script src="..." integrity="..." defer type="module"></script>  │
│    <synergos-{name} config='{...}'>                                 │
│      <!-- offline fallback si descriptor null -->                   │
│    </synergos-{name}>                                               │
├─────────────────────────────────────────────────────────────────────┤
│ 5. Browser carga el bundle, define el Custom Element, hidrata:      │
│    → upgrade reemplaza el fallback con el component real           │
└─────────────────────────────────────────────────────────────────────┘
```

## Tabla maestra (alias CMS → bundle UI)

| Alias CMS | Tag DOM | Tier | Framework(s) | Versions disponibles | Schema mirror | Rich config |
|---|---|---|---|---|---|---|
| `elementSynAcademy` | `<synergos-academy>` | module | angular | angular: latest/v0 | `SynAcademySchema` | — |
| `elementSynAccordion` | `<synergos-accordion>` | composition | angular | angular: latest/v0 | `SynAccordionSchema` | — |
| `elementSynAppLauncher` | `<synergos-app-launcher>` | module | angular | angular: latest/v0 | `SynAppLauncherSchema` | — |
| `elementSynAudioPlayer` | `<synergos-audio-player>` | module | angular | angular: latest/v0 | `SynAudioPlayerSchema` | — |
| `elementSynAutocomplete` | `<synergos-autocomplete>` | composition | angular | angular: latest/v0 | `SynAutocompleteSchema` | — |
| `elementSynAvatar` | `<synergos-avatar>` | primitive | angular | angular: latest/v0 | `SynAvatarSchema` | `AvatarElementConfig` |
| `elementSynAvatarGroup` | `<synergos-avatar-group>` | composition | angular | angular: latest/v0 | `SynAvatarGroupSchema` | — |
| `elementSynAvatarUpload` | `<synergos-avatar-upload>` | composition | angular | angular: latest/v0 | `SynAvatarUploadSchema` | — |
| `elementSynBadge` | `<synergos-badge>` | primitive | angular | angular: latest/v0 | `SynBadgeSchema` | `BadgeElementConfig` |
| `elementSynBadgeGroup` | `<synergos-badge-group>` | composition | angular | angular: latest/v0 | `SynBadgeGroupSchema` | — |
| `elementSynBlogs` | `<synergos-blogs>` | module | angular | angular: latest/v0 | `SynBlogsSchema` | — |
| `elementSynBookingWizard` | `<synergos-booking-wizard>` | module | angular | angular: latest/v0 | `SynBookingWizardSchema` | — |
| `elementSynBreadcrumb` | `<synergos-breadcrumb>` | primitive | angular | angular: latest/v0 | `SynBreadcrumbSchema` | — |
| `elementSynButtonGroup` | `<synergos-button-group>` | composition | angular | angular: latest/v0 | `SynButtonGroupSchema` | `ButtonGroupElementConfig` |
| `elementSynCalendar` | `<synergos-calendar>` | module | angular | angular: latest/v0 | `SynCalendarSchema` | — |
| `elementSynCarousel` | `<synergos-carousel>` | module | angular | angular: latest/v0 | `SynCarouselSchema` | — |
| `elementSynChartBar` | `<synergos-chart-bar>` | module | angular | angular: latest/v0 | `SynChartBarSchema` | — |
| `elementSynCodeBlock` | `<synergos-code-block>` | composition | angular | angular: latest/v0 | `SynCodeBlockSchema` | — |
| `elementSynColorPicker` | `<synergos-color-picker>` | composition | angular | angular: latest/v0 | `SynColorPickerSchema` | — |
| `elementSynColorSwatches` | `<synergos-color-swatches>` | composition | angular | angular: latest/v0 | `SynColorSwatchesSchema` | — |
| `elementSynCommentsWidget` | `<synergos-comments-widget>` | module | angular | angular: latest/v0 | `SynCommentsWidgetSchema` | — |
| `elementSynCookieConsent` | `<synergos-cookie-consent>` | module | angular | angular: latest/v0 | `SynCookieConsentSchema` | — |
| `elementSynCopyButton` | `<synergos-copy-button>` | primitive | angular | angular: latest/v0 | `SynCopyButtonSchema` | — |
| `elementSynCountdownClock` | `<synergos-countdown-clock>` | module | angular | angular: latest/v0 | `SynCountdownClockSchema` | `CountdownClockElementConfig` |
| `elementSynCountdownDigital` | `<synergos-countdown-digital>` | module | angular | angular: latest/v0 | `SynCountdownDigitalSchema` | — |
| `elementSynDataGrid` | `<synergos-data-grid>` | module | angular | angular: latest/v0 | `SynDataGridSchema` | — |
| `elementSynDatePicker` | `<synergos-date-picker>` | composition | angular | angular: latest/v0 | `SynDatePickerSchema` | — |
| `elementSynDivider` | `<synergos-divider>` | primitive | angular | angular: latest/v0 | `SynDividerSchema` | `DividerElementConfig` |
| `elementSynDrawer` | `<synergos-drawer>` | module | angular | angular: latest/v0 | `SynDrawerSchema` | — |
| `elementSynDropdown` | `<synergos-dropdown>` | composition | angular | angular: latest/v0 | `SynDropdownSchema` | — |
| `elementSynDropzone` | `<synergos-dropzone>` | module | angular | angular: latest/v0 | `SynDropzoneSchema` | — |
| `elementSynEhr` | `<synergos-ehr>` | module | angular | angular: latest/v0 | `SynEhrSchema` | — |
| `elementSynEventos` | `<synergos-eventos>` | module | angular | angular: latest/v0 | `SynEventosSchema` | — |
| `elementSynFab` | `<synergos-fab>` | primitive | angular | angular: latest/v0 | `SynFabSchema` | — |
| `elementSynFaqSection` | `<synergos-faq-section>` | module | angular | angular: latest/v0 | `SynFaqSectionSchema` | `FaqSectionElementConfig` |
| `elementSynFeatureGrid` | `<synergos-feature-grid>` | module | angular | angular: latest/v0 | `SynFeatureGridSchema` | `FeatureGridElementConfig` |
| `elementSynFileUploader` | `<synergos-file-uploader>` | module | angular | angular: latest/v0 | `SynFileUploaderSchema` | — |
| `elementSynFormStepper` | `<synergos-form-stepper>` | composition | angular | angular: latest/v0 | `SynFormStepperSchema` | — |
| `elementSynGov` | `<synergos-gov>` | module | angular | angular: latest/v0 | `SynGovSchema` | — |
| `elementSynHeroBanner` | `<synergos-hero-banner>` | module | angular | angular: latest/v0 | `SynHeroBannerSchema` | — |
| `elementSynIconLabel` | `<synergos-icon-label>` | primitive | angular | angular: latest/v0 | `SynIconLabelSchema` | — |
| `elementSynInfoBlock` | `<synergos-info-block>` | composition | angular | angular: latest/v0 | `SynInfoBlockSchema` | `InfoBlockElementConfig` |
| `elementSynKpiCard` | `<synergos-kpi-card>` | module | angular | angular: latest/v0 | `SynKpiCardSchema` | — |
| `elementSynLightboxGallery` | `<synergos-lightbox-gallery>` | module | angular | angular: latest/v0 | `SynLightboxGallerySchema` | — |
| `elementSynLivestream` | `<synergos-livestream>` | module | angular | angular: latest/v0 | `SynLivestreamSchema` | — |
| `elementSynMapPin` | `<synergos-map-pin>` | module | angular | angular: latest/v0 | `SynMapPinSchema` | — |
| `elementSynMediaText` | `<synergos-media-text>` | composition | angular | angular: latest/v0 | `SynMediaTextSchema` | `MediaTextElementConfig` |
| `elementSynModalTrigger` | `<synergos-modal-trigger>` | composition | angular | angular: latest/v0 | `SynModalTriggerSchema` | — |
| `elementSynNotificationCenter` | `<synergos-notification-center>` | module | angular | angular: latest/v0 | `SynNotificationCenterSchema` | — |
| `elementSynNotificationToast` | `<synergos-notification-toast>` | module | angular | angular: latest/v0 | `SynNotificationToastSchema` | — |
| `elementSynOEmbed` | `<synergos-oembed>` | module | angular | angular: latest/v0 | `SynOembedSchema` | — |
| `elementSynOtpInput` | `<synergos-otp-input>` | composition | angular | angular: latest/v0 | `SynOtpInputSchema` | — |
| `elementSynPagination` | `<synergos-pagination>` | composition | angular | angular: latest/v0 | `SynPaginationSchema` | — |
| `elementSynPaxSelector` | `<synergos-pax-selector>` | composition | angular | angular: latest/v0 | — | — |
| `elementSynPoll` | `<synergos-poll>` | module | angular | angular: latest/v0 | `SynPollSchema` | — |
| `elementSynPopover` | `<synergos-popover>` | primitive | angular | angular: latest/v0 | `SynPopoverSchema` | — |
| `elementSynProgressBar` | `<synergos-progress-bar>` | primitive | angular | angular: latest/v0 | `SynProgressBarSchema` | — |
| `elementSynQrCode` | `<synergos-qr-code>` | primitive | angular | angular: latest/v0 | `SynQrCodeSchema` | — |
| `elementSynQuoteAnimated` | `<synergos-quote-animated>` | module | angular | angular: latest/v0 | `SynQuoteAnimatedSchema` | — |
| `elementSynRangeSlider` | `<synergos-range-slider>` | composition | angular | angular: latest/v0 | `SynRangeSliderSchema` | — |
| `elementSynRatingStars` | `<synergos-rating-stars>` | composition | angular | angular: latest/v0 | `SynRatingStarsSchema` | — |
| `elementSynRealty` | `<synergos-realty>` | module | angular | angular: latest/v0 | `SynRealtySchema` | — |
| `elementSynRichTooltip` | `<synergos-rich-tooltip>` | composition | angular | angular: latest/v0 | `SynRichTooltipSchema` | — |
| `elementSynScrollTop` | `<synergos-scroll-top>` | primitive | angular | angular: latest/v0 | `SynScrollTopSchema` | — |
| `elementSynSearchBox` | `<synergos-search-box>` | composition | angular | angular: latest/v0 | `SynSearchBoxSchema` | — |
| `elementSynSeatMap` | `<synergos-seat-map>` | composition | angular | angular: latest/v0 | `SynSeatMapSchema` | — |
| `elementSynSelectMulti` | `<synergos-select-multi>` | composition | angular | angular: latest/v0 | `SynSelectMultiSchema` | — |
| `elementSynSeller` | `<synergos-seller>` | module | angular | angular: latest/v0 | `SynSellerSchema` | — |
| `elementSynSeparator` | `<synergos-separator>` | primitive | angular | angular: latest/v0 | `SynSeparatorSchema` | — |
| `elementSynShareBar` | `<synergos-share-bar>` | composition | angular | angular: latest/v0 | `SynShareBarSchema` | — |
| `elementSynSignaturePad` | `<synergos-signature-pad>` | composition | angular | angular: latest/v0 | `SynSignaturePadSchema` | — |
| `elementSynSkeleton` | `<synergos-skeleton>` | primitive | angular | angular: latest/v0 | `SynSkeletonSchema` | — |
| `elementSynSocialProof` | `<synergos-social-proof>` | composition | angular | angular: latest/v0 | `SynSocialProofSchema` | — |
| `elementSynSpacer` | `<synergos-spacer>` | primitive | angular | angular: latest/v0 | `SynSpacerSchema` | `SpacerElementConfig` |
| `elementSynSplitter` | `<synergos-splitter>` | composition | angular | angular: latest/v0 | `SynSplitterSchema` | — |
| `elementSynStatTicker` | `<synergos-stat-ticker>` | primitive | angular | angular: latest/v0 | `SynStatTickerSchema` | — |
| `elementSynStepper` | `<synergos-stepper>` | composition | angular | angular: latest/v0 | `SynStepperSchema` | — |
| `elementSynStorefront` | `<synergos-storefront>` | module | angular | angular: latest/v0 | `SynStorefrontSchema` | — |
| `elementSynTabs` | `<synergos-tabs>` | composition | angular | angular: latest/v0 | `SynTabsSchema` | — |
| `elementSynTag` | `<synergos-tag>` | primitive | angular | angular: latest/v0 | `SynTagSchema` | — |
| `elementSynTestimonialCarousel` | `<synergos-testimonial-carousel>` | module | angular | angular: latest/v0 | `SynTestimonialCarouselSchema` | — |
| `elementSynTestimonialSection` | `<synergos-testimonial-section>` | module | angular | angular: latest/v0 | `SynTestimonialSectionSchema` | `TestimonialSectionElementConfig` |
| `elementSynTimeline` | `<synergos-timeline>` | module | angular | angular: latest/v0 | `SynTimelineSchema` | — |
| `elementSynTimelineHorizontal` | `<synergos-timeline-horizontal>` | composition | angular | angular: latest/v0 | `SynTimelineHorizontalSchema` | — |
| `elementSynToastCenter` | `<synergos-toast-center>` | module | angular | angular: latest/v0 | `SynToastCenterSchema` | — |
| `elementSynTooltip` | `<synergos-tooltip>` | primitive | angular | angular: latest/v0 | `SynTooltipSchema` | — |
| `elementSynTourGuide` | `<synergos-tour-guide>` | module | angular | angular: latest/v0 | `SynTourGuideSchema` | — |
| `elementSynTravelShell` | `<synergos-travel-shell>` | module | angular | angular: latest/v0 | `SynTravelShellSchema` | — |
| `elementSynTreeView` | `<synergos-tree-view>` | module | angular | angular: latest/v0 | `SynTreeViewSchema` | — |
| `elementSynVideoPlayer` | `<synergos-video-player>` | module | angular | angular: latest/v0 | `SynVideoPlayerSchema` | — |

## Recomendaciones para el arquitecto

### Cuando recomiende un elementSyn*

Para cada `elementSyn{Name}`, siempre cita:

1. **Alias CMS**: `elementSyn{Name}` (lo que va en `<Composition>` references o como tipo del Block Grid)
2. **Tag DOM**: `<synergos-{kebab}>` (lo que el browser va a hidratar)
3. **Bundle URL**: `/cdn-bundles/{name}/{framework}/{slot}/main.js` (lo que el Razor emite)
4. **Shape esperado**: si tiene rich config, usar `{Pascal}ElementConfig`; si no, `Syn{Pascal}Schema`.
5. **Razor partial** (si el arquitecto va a customizar SSR): `Views/Partials/SynHost/{Pascal}.cshtml`.

### Cuando vea un schema con compIntegration

Si un ContentType compone `compIntegration`, significa que es un block
CDN-hosted (`elementSyn*`) y necesita un bundle UI publicado para hidratar.
Confirma que el alias está en esta tabla. Si NO está, el block existe en CMS
pero el bundle UI no está publicado todavía — el SSR va a emitir offline
fallback (`data-synergos-cdn-offline="true"`).

### Cuando el arquitecto pregunte "qué inputs acepta X"

Cita el `{Pascal}ElementConfig` (rich, si existe — más completo) o
`Syn{Pascal}Schema` (auto, refleja el schema CMS literal). El detalle por
cada elemento está en `ui-elements-catalog.md`.

## Edit policy

NO editar este archivo a mano — auto-regenerado.
