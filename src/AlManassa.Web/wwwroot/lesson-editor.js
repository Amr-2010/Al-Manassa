(() => {
  function init() {
    document.querySelectorAll('[data-lesson-editor]').forEach(box => {
      if (box.dataset.ready) return;
      box.dataset.ready='1';
      const editable=box.querySelector('[contenteditable]');
      const field=box.querySelector('textarea[name="content"]');
      const form=box.closest('form');
      box.querySelectorAll('[data-format]').forEach(btn => {
        btn.addEventListener('click', () => {
          editable.focus();
          document.execCommand(btn.dataset.format, false, btn.dataset.value || null);
          field.value=editable.innerHTML;
        });
      });
      editable.addEventListener('input', () => {field.value=editable.innerHTML;});
      form?.addEventListener('submit', () => {field.value=editable.innerHTML;});
    });
  }
  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',init);
  else init();
  document.addEventListener('enhancedload',init);
})();