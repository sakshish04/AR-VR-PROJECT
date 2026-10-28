// Creates the Group 05 post-condition questionnaire (SUS + NASA-TLX raw) as a Google Form.
// Paste into https://script.google.com (New project), press Run, approve access,
// then open View > Logs (or Execution log) for the form and response links.
// One submission = one participant x one condition, so each person fills it twice.

function createGroup05Form() {
  var form = FormApp.create('Group 05 VoiceVR - Post-condition questionnaire (SUS + NASA-TLX)');
  form.setDescription(
    'Fill this in right after you finish each condition. Answer about the condition you just used. ' +
    'Use only your participant code (e.g. P01), never your name. Your answers are anonymous and used only for the IVRAR course project.');
  form.setCollectEmail(false);
  form.setAllowResponseEdits(false);

  form.addTextItem()
      .setTitle('Participant code (e.g. P01)')
      .setRequired(true)
      .setValidation(FormApp.createTextValidation()
          .requireTextMatchesPattern('^P[0-9]{2}$')
          .setHelpText('Format: P followed by two digits, e.g. P01')
          .build());

  form.addMultipleChoiceItem()
      .setTitle('Which condition did you just use?')
      .setChoiceValues(['Voice + gaze', 'Controller'])
      .setRequired(true);

  form.addDateItem().setTitle('Date of this session').setRequired(true);

  // System Usability Scale (Brooke, 1996): 10 standard items, 1-5.
  form.addPageBreakItem().setTitle('Part A - System Usability Scale')
      .setHelpText('1 = Strongly disagree, 5 = Strongly agree');
  var sus = [
    'I think that I would like to use this system frequently.',
    'I found the system unnecessarily complex.',
    'I thought the system was easy to use.',
    'I think that I would need the support of a technical person to be able to use this system.',
    'I found the various functions in this system were well integrated.',
    'I thought there was too much inconsistency in this system.',
    'I would imagine that most people would learn to use this system very quickly.',
    'I found the system very cumbersome to use.',
    'I felt very confident using the system.',
    'I needed to learn a lot of things before I could get going with this system.'
  ];
  sus.forEach(function (text, i) {
    form.addScaleItem()
        .setTitle('SUS ' + (i + 1) + '. ' + text)
        .setBounds(1, 5)
        .setLabels('Strongly disagree', 'Strongly agree')
        .setRequired(true);
  });

  // NASA-TLX raw (Hart & Staveland, 1988): 6 subscales, 0-100 in steps of 5 (21-point scale).
  form.addPageBreakItem().setTitle('Part B - NASA Task Load Index')
      .setHelpText('Rate the task you just did. 0 = very low, 100 = very high (for Performance: 0 = perfect, 100 = failure).');
  var tlx = [
    ['Mental demand', 'How mentally demanding was the task?', 'Very low', 'Very high'],
    ['Physical demand', 'How physically demanding was the task?', 'Very low', 'Very high'],
    ['Temporal demand', 'How hurried or rushed was the pace of the task?', 'Very low', 'Very high'],
    ['Performance', 'How successful were you in accomplishing what you were asked to do?', 'Perfect', 'Failure'],
    ['Effort', 'How hard did you have to work to accomplish your level of performance?', 'Very low', 'Very high'],
    ['Frustration', 'How insecure, discouraged, irritated, stressed, and annoyed were you?', 'Very low', 'Very high']
  ];
  var steps = [];
  for (var v = 0; v <= 100; v += 5) steps.push(String(v));
  tlx.forEach(function (t) {
    form.addMultipleChoiceItem()
        .setTitle(t[0] + ' - ' + t[1] + ' (0 = ' + t[2] + ', 100 = ' + t[3] + ')')
        .setChoiceValues(steps)
        .setRequired(true);
  });

  form.addParagraphTextItem().setTitle('Any comments? (optional)');

  var sheet = SpreadsheetApp.create('Group 05 questionnaire responses');
  form.setDestination(FormApp.DestinationType.SPREADSHEET, sheet.getId());

  Logger.log('Send to participants: ' + form.getPublishedUrl());
  Logger.log('Edit form:            ' + form.getEditUrl());
  Logger.log('Responses sheet:      ' + sheet.getUrl());
}
