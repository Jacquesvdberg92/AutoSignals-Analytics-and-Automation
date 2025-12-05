(function () {
    "use strict";

    let checkAll = document.querySelector('.check-all');
    checkAll.addEventListener('click', checkAllFn)

    function checkAllFn() {
        if (checkAll.checked) {
            document.querySelectorAll('.task-checkbox input').forEach(function (e) {
                e.closest('.todo-box').classList.add('selected');
                e.checked = true;
            });
        }
        else {
            document.querySelectorAll('.task-checkbox input').forEach(function (e) {
                e.closest('.todo-box').classList.remove('selected');
                e.checked = false;
            });
        }
    }

    /* draggable js */
    dragula([document.getElementById('todo-drag')],{
        moves: function (el, container, handle) {
            return handle.classList.contains('todo-handle');
          }
    });

})();