const shots = {
  handles: {
    src: 'assets/ginkyo-handles.png',
    alt: 'Ginkyo Sekai in pen tool mode, with a curved FSlider, selected anchor, visible Bézier handles and Catch preview.',
    caption: 'Ginkyo Sekai (feat. Kasane Teto) — DJ Raisei · -Ken [Reminiscence]. FSlider editing with an anchor and Bézier handles.'
  },
  controls: {
    src: 'assets/ginkyo-control-points.png',
    alt: 'Ginkyo Sekai in control-point mode with a selected FSlider, a moved corner, and the resulting Catch preview.',
    caption: 'Ginkyo Sekai (feat. Kasane Teto) — DJ Raisei · -Ken [Reminiscence]. FSlider control-point editing with the resulting Catch preview.'
  }
};
document.querySelectorAll('[data-slider]').forEach(button => {
  button.addEventListener('click', () => {
    const shot = shots[button.dataset.slider];
    const image = document.querySelector('#slider-image');
    const link = document.querySelector('#slider-image-link');
    image.src = shot.src;
    image.alt = shot.alt;
    link.href = shot.src;
    link.dataset.caption = shot.caption;
    document.querySelectorAll('[data-slider]').forEach(other => other.setAttribute('aria-pressed', String(other === button)));
  });
});
const dialog = document.querySelector('#image-dialog');
let imageTrigger;
document.querySelectorAll('[data-lightbox]').forEach(link => {
  link.addEventListener('click', event => {
    if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    imageTrigger = link;
    document.querySelector('#dialog-image').src = link.href;
    document.querySelector('#dialog-image').alt = link.querySelector('img').alt;
    document.querySelector('#dialog-caption').textContent = link.dataset.caption;
    dialog.showModal();
    document.querySelector('#close-dialog').focus();
  });
});
document.querySelector('#close-dialog').addEventListener('click', () => dialog.close());
dialog.addEventListener('click', event => {
  const rect = dialog.getBoundingClientRect();
  if (event.target === dialog && (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom)) dialog.close();
});
dialog.addEventListener('close', () => imageTrigger?.focus());
