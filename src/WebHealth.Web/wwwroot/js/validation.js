(function () {
    'use strict';

    if (!window.jQuery || !window.jQuery.validator || !window.jQuery.validator.unobtrusive) {
        return;
    }

    var $ = window.jQuery;

    function messageFor(element) {
        return $(element).closest('form').find('[data-valmsg-for="' + element.name + '"]');
    }

    $.validator.setDefaults({
        errorClass: 'field-validation-error',
        validClass: 'field-validation-valid',
        highlight: function (element) {
            $(element).attr('aria-invalid', 'true');
        },
        unhighlight: function (element) {
            $(element).removeAttr('aria-invalid');
        },
        errorPlacement: function (error, element) {
            var target = messageFor(element[0]);
            if (target.length) {
                target.empty().append(error);
                return;
            }
            error.insertAfter(element);
        }
    });

    function parse(root) {
        $(root).find('form').addBack('form').each(function () {
            $(this).removeData('validator').removeData('unobtrusiveValidation');
            $.validator.unobtrusive.parse(this);
        });
    }

    document.addEventListener('webhealth:fragment-ready', function (event) {
        if (event.detail && event.detail.root) {
            parse(event.detail.root);
        }
    });
})();
