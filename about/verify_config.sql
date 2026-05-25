USE GlassWarehouseDB;
SELECT KeyName, `Describe`, `Type`, `Value`, Address
FROM config
ORDER BY
  CASE WHEN KeyName LIKE 'Addr_%' THEN 1 ELSE 0 END,
  KeyName;
SELECT COUNT(*) AS total_rows FROM config;
