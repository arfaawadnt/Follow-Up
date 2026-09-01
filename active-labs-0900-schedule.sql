-- One-time: give every Active lab a 09:00 visit slot on all 7 days.
-- Schedule jsonb shape matches VisitScheduleConverter: {"Days":[Sun=0..Sat=6],"Times":["HH:mm:ss"]}
\echo '== Active labs before =='
SELECT count(*) AS active_total,
       count(*) FILTER (WHERE jsonb_array_length(schedule->'Days') > 0) AS active_with_schedule
FROM laboratory WHERE status = 'Active';

\echo '== Snapshot affected rows to active_labs_schedule_before.csv (for rollback) =='
\copy (SELECT id, code, schedule::text FROM laboratory WHERE status = 'Active') TO 'active_labs_schedule_before.csv' CSV HEADER

BEGIN;
UPDATE laboratory
SET schedule = '{"Days":[0,1,2,3,4,5,6],"Times":["09:00:00"]}'::jsonb
WHERE status = 'Active';

\echo '== Rows now set to 09:00 all-days (should equal active_total) =='
SELECT count(*) AS now_0900_all_days
FROM laboratory
WHERE status = 'Active'
  AND schedule = '{"Days":[0,1,2,3,4,5,6],"Times":["09:00:00"]}'::jsonb;
COMMIT;
