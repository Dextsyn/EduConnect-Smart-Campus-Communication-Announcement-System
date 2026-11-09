// Client side of [PersonName] (PersonInput.cs): a name with a digit gets
// the "no numbers" message, any other mismatch the letters-only one.
(function ($) {
    if (!$ || !$.validator || !$.validator.unobtrusive) return;

    const hasDigit = v => /[0-9]/.test(v);
    const normalize = v => v.trim().replace(/\s+/g, ' ');

    $.validator.addMethod('personname', function (value, element, params) {
        const v = normalize(value);
        if (!v) return true;   // blank is [Required]'s job
        return !hasDigit(v) && new RegExp(params.pattern).test(v);
    });

    $.validator.unobtrusive.adapters.add('personname', ['pattern', 'digits'], function (options) {
        options.rules.personname = options.params;
        const general = options.message;
        options.messages.personname = function (params, element) {
            return hasDigit(element.value) ? params.digits : general;
        };
    });
})(window.jQuery);
